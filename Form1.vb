Imports System.IO
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports HtmlAgilityPack
Imports Newtonsoft.Json

Public Class Form1

    Private httpClient As New HttpClient()
    Private failIndex As Integer = 0 ' 全局失败编号
    Private isPaused As Boolean = False
    Private updateData As New Dictionary(Of String, Dictionary(Of String, Integer))
    ' 阶段名
    Private stages As String() = {"初始", "一破", "三破", "满破"}
    Private Structure ItemInfo
        Public Id As String
        Public Name As String
        Public NameLink As String
    End Structure

    ' 自动获取角色链接并调试输出（解析 override_data 方式），不去重
    Private Async Function GetAllRoleLinksFromWiki() As Task(Of List(Of String))
        Dim url As String = "https://fgo.wiki/w/%E8%8B%B1%E7%81%B5%E5%9B%BE%E9%89%B4"
        Dim html As String = Await httpClient.GetStringAsync(url)
        Dim roleLinks As New List(Of String)

        Dim m = Regex.Match(html, "override_data\s*=\s*""([\s\S]*?)"";")
        If Not m.Success Then
            ListBox1.Items.Add("[调试] 未找到 override_data")
            Return roleLinks
        End If
        Dim data = m.Groups(1).Value.Replace("\n", vbLf)

        Dim matches = Regex.Matches(data, "name_link=(.+)")
        For Each match As Match In matches
            Dim link = match.Groups(1).Value.Trim()
            ' 处理特殊字符
            link = link.Replace("“", """").Replace("”", """").Replace(" ", "_").Replace("・", "·")
            Dim fullUrl = $"https://fgo.wiki/w/{link}"
            roleLinks.Add(fullUrl)
            ListBox1.Items.Add($"[调试] 角色链接: {fullUrl}")
        Next
        ListBox1.Items.Add($"[信息] 共提取到 {roleLinks.Count} 个角色链接")
        Return roleLinks
    End Function

    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        failIndex = 0
        Button1.Enabled = False
        Button2.Enabled = False
        Button3.Enabled = False
        Button4.Enabled = False
        ListBox1.Items.Clear()
        ProgressBar1.Value = 0
        ProgressBar2.Value = 0

        ListBox1.Items.Add("正在获取角色信息列表...")
        Dim roleList = Await GetAllRoleInfoFromWiki()
        ListBox1.Items.Add($"共获取到 {roleList.Count} 个角色")
        ListBox1.TopIndex = ListBox1.Items.Count - 1

        ' 写入角色链接列表到根目录 juese.txt
        Dim juesePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "juese.txt")
        File.WriteAllLines(juesePath, roleList.Select(Function(r) $"https://fgo.wiki/w/{r.NameLink}"), System.Text.Encoding.UTF8)
        ListBox1.Items.Add($"角色链接列表已保存到 {juesePath}")

        Dim rootPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images\角色")
        ProgressBar2.Maximum = roleList.Count

        ' 读取已有 update.ini，避免覆盖礼装数据
        Dim updateIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.ini")
        updateData = ReadUpdateIni(updateIniPath)

        Dim idx As Integer = 0
        For Each info In roleList
            While isPaused
                Await Task.Delay(200)
                Application.DoEvents()
            End While

            idx += 1
            Label1.Text = $"{idx}/{roleList.Count}"

            Dim dirName = $"{info.Id.PadLeft(4, "0"c)}.{info.Name}"
            Dim roleDir = Path.Combine(rootPath, dirName)
            ' 检查目录是否存在，存在则停止整个任务
            If Directory.Exists(roleDir) Then
                ListBox1.Items.Add($"检测到目录已存在：{dirName}，停止下载任务。")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Exit For
            End If
            Directory.CreateDirectory(roleDir)
            ListBox1.Items.Add($"[{idx}/{roleList.Count}] 正在处理：{dirName}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1

            ProgressBar1.Value = 0
            ProgressBar1.Maximum = 2

            Dim detailPageUrl = $"https://fgo.wiki/w/{info.NameLink}"
            Await DownloadAllGraphpickerImages(detailPageUrl, roleDir, dirName)
            ProgressBar1.Value += 1
            Application.DoEvents()
            ' 下载各阶段图标与战斗形象
            Await DownloadRoleIconsAndBattleImages(detailPageUrl, roleDir, dirName)


            Await DownloadAllGraphpickerHistoryImages(detailPageUrl, roleDir, dirName)
            ProgressBar1.Value += 1
            Application.DoEvents()
            ' 保存角色资料
            Await SaveRoleProfile(detailPageUrl, roleDir)

            '保存语音
            Await DownloadRoleVoice(detailPageUrl, roleDir)

            ' 统计历史图片数量并写入 updateData
            Dim stageHistoryCount = Await GetGraphpickerHistoryCount(detailPageUrl)
            If Not updateData.ContainsKey(dirName) Then
                updateData(dirName) = New Dictionary(Of String, Integer)
            End If
            For Each stage In stageHistoryCount.Keys
                updateData(dirName)(stage) = stageHistoryCount(stage)
            Next

            ProgressBar2.Value = idx
            Application.DoEvents()
        Next

        ' 角色循环结束后
        WriteUpdateIni(updateIniPath, updateData)

        ListBox1.Items.Add("全部下载完成。")
        ListBox1.TopIndex = ListBox1.Items.Count - 1
        Button1.Enabled = True
        Button2.Enabled = True
        'Button3.Enabled = True
        'Button4.Enabled = True
    End Sub

    ' 下载原始大图
    Private Async Function DownloadImageFromWiki(filePageUrl As String, savePath As String, roleName As String, stage As String) As Task
        Try
            ListBox1.Items.Add($"  [调试] 文件页面: {filePageUrl}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1

            Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim aNode = doc.DocumentNode.SelectSingleNode("//div[@class='fullMedia']/p/a[@class='internal']")
            If aNode Is Nothing Then
                ListBox1.Items.Add($"  [调试] 未找到原始图片链接: {filePageUrl}")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                failIndex += 1
                WriteFailLog(failIndex, roleName, filePageUrl)
                Return
            End If
            Dim realImgUrl = aNode.GetAttributeValue("href", "")
            If realImgUrl.StartsWith("//") Then
                realImgUrl = "https:" & realImgUrl
            ElseIf realImgUrl.StartsWith("/") Then
                realImgUrl = "https://fgo.wiki" & realImgUrl
            End If
            ListBox1.Items.Add($"  [下载] 原始图片地址: {realImgUrl}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1

            Dim imgBytes = Await httpClient.GetByteArrayAsync(realImgUrl)
            File.WriteAllBytes(savePath, imgBytes)
            ListBox1.Items.Add($"  下载成功: {roleName}{stage}.png")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] {roleName}{stage}: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
            failIndex += 1
            WriteFailLog(failIndex, roleName, filePageUrl)
        End Try
    End Function

    ' 写入失败日志
    Private Sub WriteFailLog(index As Integer, roleName As String, url As String)
        Dim logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "download_failed.log")
        Dim line = $"{index},{roleName},{url}{Environment.NewLine}"
        File.AppendAllText(logPath, line, System.Text.Encoding.UTF8)
    End Sub

    ' 获取所有礼装中文名
    Private Async Function GetAllCENameFromWiki() As Task(Of List(Of String))
        Dim url As String = "https://fgo.wiki/w/%E7%A4%BC%E8%A3%85%E5%9B%BE%E9%89%B4"
        Dim html As String = Await httpClient.GetStringAsync(url)
        Dim ceNames As New List(Of String)

        Dim m = Regex.Match(html, "override_data\s*=\s*""([\s\S]*?)"";")
        If Not m.Success Then
            ListBox1.Items.Add("[调试] 未找到 override_data")
            Return ceNames
        End If
        Dim data = m.Groups(1).Value.Replace("\n", vbLf)

        Dim matches = Regex.Matches(data, "name=(.+)")
        For Each match As Match In matches
            Dim name = match.Groups(1).Value.Trim()
            name = name.Replace("“", """").Replace("”", """").Replace(" ", "_").Replace("・", "·")
            ceNames.Add(name) ' 不去重
            ListBox1.Items.Add($"[调试] 礼装名: {name}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        Next
        ListBox1.Items.Add($"[调试] 共提取到 {ceNames.Count} 个礼装名")
        ListBox1.TopIndex = ListBox1.Items.Count - 1
        Return ceNames
    End Function

    ' 下载礼装图鉴图片
    ' 下载礼装图鉴图片并写入 update.ini
    Private Async Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        failIndex = 0
        Button1.Enabled = False
        Button2.Enabled = False
        Button3.Enabled = False
        Button4.Enabled = False
        ListBox1.Items.Clear()
        ProgressBar1.Value = 0
        ProgressBar2.Value = 0

        ListBox1.Items.Add("正在获取礼装列表...")
        Dim ceList = Await GetAllCEInfoFromWiki()
        ListBox1.Items.Add($"共获取到 {ceList.Count} 个礼装")
        ListBox1.TopIndex = ListBox1.Items.Count - 1

        Dim rootPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images\礼装图鉴")
        Directory.CreateDirectory(rootPath)
        ProgressBar2.Maximum = ceList.Count

        ' 读取已有 update.ini，避免覆盖角色数据
        Dim updateIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.ini")
        updateData = ReadUpdateIni(updateIniPath)

        Dim idx As Integer = 0
        For Each info In ceList
            While isPaused
                Await Task.Delay(200)
                Application.DoEvents()
            End While

            idx += 1
            Label1.Text = $"{idx}/{ceList.Count}"

            Dim saveName = $"{info.Id.PadLeft(4, "0"c)}.{info.Name}.png"
            Dim ceDir = Path.Combine(rootPath, $"{info.Id.PadLeft(4, "0"c)}.{info.Name}")
            ' 检查目录是否存在，存在则停止整个任务
            If Directory.Exists(ceDir) Then
                ListBox1.Items.Add($"检测到目录已存在：{ceDir}，停止下载任务。")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Exit For
            End If

            ListBox1.Items.Add($"[{idx}/{ceList.Count}] 正在处理：{saveName}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1

            Dim detailPageUrl = $"https://fgo.wiki/w/{info.NameLink}"
            Dim savePath = Path.Combine(rootPath, saveName)
            Await DownloadCEOriginalImage(detailPageUrl, savePath)

            ' 保存解说
            Await SaveCECommentary(detailPageUrl, info.Id, info.Name, rootPath)

            ' 历史图片
            Await DownloadAllCEGraphpickerHistoryImages(detailPageUrl, rootPath, $"{info.Id.PadLeft(4, "0"c)}.{info.Name}")

            ' 统计历史图片数量并写入 updateData
            Dim stageHistoryCount = Await GetGraphpickerHistoryCount(detailPageUrl)
            Dim key = $"{info.Id.PadLeft(4, "0"c)}.{info.Name}"
            If Not updateData.ContainsKey(key) Then
                updateData(key) = New Dictionary(Of String, Integer)
            End If
            For Each stage In stageHistoryCount.Keys
                updateData(key)(stage) = stageHistoryCount(stage)
            Next

            ProgressBar2.Value = idx
            Application.DoEvents()
        Next

        ' 礼装循环结束后写入 update.ini
        WriteUpdateIni(updateIniPath, updateData)

        ListBox1.Items.Add("全部下载完成。")
        ListBox1.TopIndex = ListBox1.Items.Count - 1
        Button1.Enabled = True
        Button2.Enabled = True
        'Button3.Enabled = True
        'Button4.Enabled = True
    End Sub
    Private Async Function DownloadAllGraphpickerImages(detailPageUrl As String, saveDir As String, roleName As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim graphDiv = doc.DocumentNode.SelectSingleNode("//div[@class='graphpicker']")
            If graphDiv Is Nothing Then
                Return
            End If

            Dim imgNodes = graphDiv.SelectNodes(".//img")
            If imgNodes Is Nothing Then
                ListBox1.Items.Add("[调试] 没有找到立绘图片")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Return
            End If

            For Each imgNode In imgNodes
                Dim alt As String = imgNode.GetAttributeValue("alt", "")
                Dim dataSrcSet As String = imgNode.GetAttributeValue("data-srcset", "")
                Dim realImgUrl As String = Nothing

                ' 优先取2x原图
                If Not String.IsNullOrEmpty(dataSrcSet) AndAlso dataSrcSet.Contains("2x") Then
                    Dim match = Regex.Match(dataSrcSet, "(https://[^\s]+) 2x")
                    If match.Success Then
                        realImgUrl = match.Groups(1).Value
                    End If
                End If
                ' 退而取data-src
                If String.IsNullOrEmpty(realImgUrl) Then
                    realImgUrl = imgNode.GetAttributeValue("data-src", "")
                End If
                If String.IsNullOrEmpty(realImgUrl) Then Continue For

                Dim safeAlt = alt.Replace("“", """").Replace("”", """").Replace(" ", "_").Replace("・", "·").Replace("/", "_")
                Dim savePath = Path.Combine(saveDir, safeAlt)
                If File.Exists(savePath) Then
                    ListBox1.Items.Add($"  已存在，跳过: {safeAlt}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                    Continue For
                End If
                Try
                    Dim imgBytes = Await httpClient.GetByteArrayAsync(realImgUrl)
                    File.WriteAllBytes(savePath, imgBytes)
                    ListBox1.Items.Add($"  下载成功: {safeAlt}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                Catch ex As Exception
                    ListBox1.Items.Add($"  [跳过] {safeAlt}: {ex.Message}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                    failIndex += 1
                    WriteFailLog(failIndex, roleName, realImgUrl)
                End Try
            Next
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] graphpicker: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function
    ' 角色历史图片去重下载，记录到 history_image_record.json
    Private Async Function DownloadAllFileHistoryImages(filePageUrl As String, saveDir As String, baseFileName As String, roleName As String, Optional mainImageUrl As String = Nothing) As Task
        Dim debugImgUrls As New List(Of String)
        Try
            ' 1. 读取历史图片记录
            Dim recordPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history_image_record.json")
            Dim historyImageRecord As New Dictionary(Of String, Dictionary(Of String, List(Of String)))
            If File.Exists(recordPath) Then
                Dim json = File.ReadAllText(recordPath, System.Text.Encoding.UTF8)
                historyImageRecord = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, Dictionary(Of String, List(Of String))))(json)
            End If

            ' 2. 采集网页所有历史图片URL
            Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim historyDiv = doc.DocumentNode.SelectSingleNode("//div[@id='mw-imagepage-section-filehistory']")
            If historyDiv Is Nothing Then Return

            Dim table = historyDiv.SelectSingleNode(".//table[contains(@class,'filehistory')]")
            If table Is Nothing Then Return

            Dim rows = table.SelectNodes(".//tr[td]")
            If rows Is Nothing Then Return

            Dim historyDir = Path.Combine(saveDir, "历史版本")
            Directory.CreateDirectory(historyDir)

            ' 阶段名从 baseFileName 里提取
            Dim stageName As String = baseFileName
            If roleName IsNot Nothing AndAlso baseFileName.StartsWith(roleName) Then
                stageName = baseFileName.Substring(roleName.Length)
            End If

            ' 读取本地已下载URL
            Dim localUrls As New List(Of String)
            If historyImageRecord.ContainsKey(roleName) AndAlso historyImageRecord(roleName).ContainsKey(stageName) Then
                localUrls = historyImageRecord(roleName)(stageName)
            End If

            ' 3. 遍历网页历史图片，下载本地没有的
            Dim idx As Integer = 0
            Dim urlSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each row In rows
                Dim tdNodes = row.SelectNodes(".//td")
                If tdNodes Is Nothing OrElse tdNodes.Count < 2 Then Continue For
                Dim aNode = tdNodes(1).SelectSingleNode(".//a")
                If aNode Is Nothing Then Continue For
                Dim imgUrl = aNode.GetAttributeValue("href", "")
                debugImgUrls.Add(imgUrl)
                If Not imgUrl.StartsWith("https://media.fgo.wiki/") Then Continue For
                If Not (imgUrl.ToLower().EndsWith(".png") OrElse imgUrl.ToLower().EndsWith(".jpg") OrElse imgUrl.ToLower().EndsWith(".jpeg") OrElse imgUrl.ToLower().EndsWith(".gif")) Then Continue For
                If urlSet.Contains(imgUrl) Then Continue For
                If mainImageUrl IsNot Nothing AndAlso imgUrl = mainImageUrl Then Continue For
                urlSet.Add(imgUrl)
                idx += 1
                Dim ext = Path.GetExtension(imgUrl)
                Dim saveName = $"{baseFileName}_历史{idx}{ext}"
                Dim savePath = Path.Combine(historyDir, saveName)

                ' 只下载本地没有的
                If localUrls.Contains(imgUrl) AndAlso File.Exists(savePath) Then
                    Continue For
                End If

                Try
                    Using handler As New HttpClientHandler()
                        handler.AllowAutoRedirect = True
                        Using client As New HttpClient(handler)
                            Dim request = New HttpRequestMessage(HttpMethod.Get, imgUrl)
                            request.Headers.Referrer = New Uri(filePageUrl)
                            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36")
                            Dim response = Await client.SendAsync(request)
                            If response.StatusCode = Net.HttpStatusCode.NotFound Then
                                ListBox1.Items.Add($"  [404] 链接不存在: {imgUrl}")
                                ListBox1.TopIndex = ListBox1.Items.Count - 1
                                failIndex += 1
                                WriteFailLog(failIndex, roleName, imgUrl)
                                Continue For
                            End If
                            response.EnsureSuccessStatusCode()
                            Dim imgBytes = Await response.Content.ReadAsByteArrayAsync()
                            File.WriteAllBytes(savePath, imgBytes)
                            ListBox1.Items.Add($"  下载历史版本: {saveName}")
                            ListBox1.TopIndex = ListBox1.Items.Count - 1
                        End Using
                    End Using
                    Await Task.Delay(500)
                    ' 下载成功后添加到本地记录
                    If Not localUrls.Contains(imgUrl) Then localUrls.Add(imgUrl)
                Catch ex As Exception
                    ListBox1.Items.Add($"  [跳过历史] {saveName} ({imgUrl}): {ex.Message}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                    failIndex += 1
                    WriteFailLog(failIndex, roleName, imgUrl)
                End Try
            Next

            ' 4. 更新本地记录
            If Not historyImageRecord.ContainsKey(roleName) Then historyImageRecord(roleName) = New Dictionary(Of String, List(Of String))
            historyImageRecord(roleName)(stageName) = localUrls
            Dim jsonOut = Newtonsoft.Json.JsonConvert.SerializeObject(historyImageRecord, Newtonsoft.Json.Formatting.Indented)
            File.WriteAllText(recordPath, jsonOut, System.Text.Encoding.UTF8)
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 文件历史: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function


    Private Sub ButtonPause_Click(sender As Object, e As EventArgs) Handles ButtonPause.Click
        isPaused = Not isPaused
        Button1.Enabled = True
        Button2.Enabled = True
        Button3.Enabled = True
        Button4.Enabled = True
        If isPaused Then
            ButtonPause.Text = "继续"
            ListBox1.Items.Add("已暂停。")
        Else
            ButtonPause.Text = "暂停"
            ListBox1.Items.Add("继续下载。")
        End If
        ListBox1.TopIndex = ListBox1.Items.Count - 1
    End Sub
    ' 下载礼装所有阶段的历史图片
    Private Async Function DownloadAllCEGraphpickerHistoryImages(detailPageUrl As String, saveDir As String, ceName As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim graphDiv = doc.DocumentNode.SelectSingleNode("//div[@class='graphpicker']")
            If graphDiv Is Nothing Then
                ListBox1.Items.Add("[调试] 没有找到 graphpicker")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Return
            End If

            Dim aNodes = graphDiv.SelectNodes(".//a[@class='image']")
            If aNodes Is Nothing Then
                ListBox1.Items.Add("[调试] 没有找到礼装立绘链接")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Return
            End If

            For Each aNode In aNodes
                Dim href = aNode.GetAttributeValue("href", "")
                If String.IsNullOrEmpty(href) Then Continue For
                Dim stageFilePageUrl As String
                If href.StartsWith("http") Then
                    stageFilePageUrl = href
                Else
                    stageFilePageUrl = "https://fgo.wiki" & href
                End If

                ' 获取阶段名
                Dim imgNode = aNode.SelectSingleNode(".//img")
                Dim stageName = ""
                If imgNode IsNot Nothing Then
                    stageName = Path.GetFileNameWithoutExtension(imgNode.GetAttributeValue("alt", ""))
                End If
                If String.IsNullOrEmpty(stageName) Then
                    stageName = "未知阶段"
                End If

                ' 下载该阶段的所有历史图片
                Await DownloadAllFileHistoryImages(stageFilePageUrl, saveDir, $"{ceName}{stageName}", ceName)
            Next
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 礼装graphpicker历史: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function




    '更新方法
    Private Function ReadUpdateIni(path As String) As Dictionary(Of String, Dictionary(Of String, Integer))
        Dim result As New Dictionary(Of String, Dictionary(Of String, Integer))
        If Not File.Exists(path) Then Return result
        Dim lines = File.ReadAllLines(path, System.Text.Encoding.UTF8)
        Dim currentRole As String = Nothing
        For Each line In lines
            If line.StartsWith("[") AndAlso line.EndsWith("]") Then
                currentRole = line.Trim("["c, "]"c)
                If Not result.ContainsKey(currentRole) Then
                    result(currentRole) = New Dictionary(Of String, Integer)
                End If
            ElseIf line.StartsWith("历史图片=") AndAlso currentRole IsNot Nothing Then
                Dim parts = line.Substring("历史图片=".Length).Split(","c)
                For Each part In parts
                    Dim kv = part.Split("-"c)
                    If kv.Length = 2 Then
                        result(currentRole)(kv(0)) = Integer.Parse(kv(1))
                    End If
                Next
            End If
        Next
        Return result
    End Function


    Private Sub WriteUpdateIni(path As String, data As Dictionary(Of String, Dictionary(Of String, Integer)))
        Using sw As New StreamWriter(path, False, System.Text.Encoding.UTF8)
            For Each role In data.Keys
                sw.WriteLine($"[{role}]")
                Dim items = data(role).Select(Function(kv) $"{kv.Key}-{kv.Value}")
                sw.WriteLine("历史图片=" & String.Join(",", items))
            Next
        End Using
    End Sub


    Private Async Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        Button1.Enabled = False
        Button2.Enabled = False
        Button3.Enabled = False
        Button4.Enabled = False
        ListBox1.Items.Clear()
        ListBox1.Items.Add("正在检查增量更新...")
        Dim updateIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.ini")
        Dim updateData = ReadUpdateIni(updateIniPath)
        Dim roleLinks = Await GetAllRoleLinksFromWiki()
        Dim updated As Boolean = False

        For Each roleLink In roleLinks
            Dim roleName = roleLink.Substring(roleLink.LastIndexOf("/") + 1)
            Dim roleDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images\角色", roleName)
            Directory.CreateDirectory(roleDir)
            Dim detailPageUrl = roleLink

            ' 解析各阶段历史图片数量
            Dim stageHistoryCount = Await GetGraphpickerHistoryCount(detailPageUrl)
            If Not updateData.ContainsKey(roleName) Then
                updateData(roleName) = New Dictionary(Of String, Integer)
            End If

            Dim needUpdate As Boolean = False
            For Each stage In stageHistoryCount.Keys
                Dim newCount = stageHistoryCount(stage)
                Dim oldCount = If(updateData(roleName).ContainsKey(stage), updateData(roleName)(stage), 0)
                If newCount > oldCount Then
                    needUpdate = True
                    ' 下载新增历史图片
                    Await DownloadGraphpickerHistoryStage(detailPageUrl, roleDir, roleName, stage, oldCount + 1, newCount)
                    updateData(roleName)(stage) = newCount
                End If
            Next

            If needUpdate Then
                ListBox1.Items.Add($"[更新] {roleName} 有新历史图片，已下载并记录。")
                updated = True
            End If
        Next

        If updated Then
            WriteUpdateIni(updateIniPath, updateData)
            ListBox1.Items.Add("update.ini 已更新。")
        Else
            ListBox1.Items.Add("无增量更新。")
        End If
        Button1.Enabled = True
        Button2.Enabled = True
        Button3.Enabled = True
        Button4.Enabled = True
    End Sub



    Private Async Function GetGraphpickerHistoryCount(detailPageUrl As String) As Task(Of Dictionary(Of String, Integer))
        Dim result As New Dictionary(Of String, Integer)
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            Dim graphDiv = doc.DocumentNode.SelectSingleNode("//div[@class='graphpicker']")
            If graphDiv Is Nothing Then Return result
            Dim aNodes = graphDiv.SelectNodes(".//a[@class='image']")
            If aNodes Is Nothing Then Return result
            For Each aNode In aNodes
                Dim href = aNode.GetAttributeValue("href", "")
                If String.IsNullOrEmpty(href) Then Continue For
                Dim stageFilePageUrl As String = If(href.StartsWith("http"), href, "https://fgo.wiki" & href)
                Dim imgNode = aNode.SelectSingleNode(".//img")
                Dim stageName = If(imgNode IsNot Nothing, Path.GetFileNameWithoutExtension(imgNode.GetAttributeValue("alt", "")), "未知阶段")
                ' 获取历史图片数量
                Dim count = Await GetFileHistoryCount(stageFilePageUrl)
                result(stageName) = count
            Next
        Catch ex As HttpRequestException
            ' 404 或其他网络错误，返回空
            ListBox1.Items.Add($"  [跳过] 获取历史图片数量失败: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
            Return result
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 获取历史图片数量异常: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
            Return result
        End Try
        Return result
    End Function


    Private Async Function GetFileHistoryCount(filePageUrl As String) As Task(Of Integer)
        Try
            Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            Dim historyDiv = doc.DocumentNode.SelectSingleNode("//div[@id='mw-imagepage-section-filehistory']")
            If historyDiv Is Nothing Then Return 0
            Dim table = historyDiv.SelectSingleNode(".//table[contains(@class,'filehistory')]")
            If table Is Nothing Then Return 0
            Dim rows = table.SelectNodes(".//tr[td]")
            If rows Is Nothing Then Return 0
            Return rows.Count
        Catch ex As HttpRequestException
            ' 404 或其他网络错误
            ListBox1.Items.Add($"  [跳过] 获取文件历史失败: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
            Return 0
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 获取文件历史异常: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
            Return 0
        End Try
    End Function




    Private Async Function DownloadGraphpickerHistoryStage(detailPageUrl As String, saveDir As String, roleName As String, stageName As String, fromIdx As Integer, toIdx As Integer) As Task
        ' 只下载 fromIdx+1 到 toIdx 的历史图片
        Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
        Dim doc As New HtmlDocument()
        doc.LoadHtml(html)
        Dim graphDiv = doc.DocumentNode.SelectSingleNode("//div[@class='graphpicker']")
        If graphDiv Is Nothing Then Return
        Dim aNodes = graphDiv.SelectNodes(".//a[@class='image']")
        If aNodes Is Nothing Then Return
        For Each aNode In aNodes
            Dim imgNode = aNode.SelectSingleNode(".//img")
            Dim stage = If(imgNode IsNot Nothing, Path.GetFileNameWithoutExtension(imgNode.GetAttributeValue("alt", "")), "未知阶段")
            If stage <> stageName Then Continue For
            Dim href = aNode.GetAttributeValue("href", "")
            If String.IsNullOrEmpty(href) Then Continue For
            Dim stageFilePageUrl As String = If(href.StartsWith("http"), href, "https://fgo.wiki" & href)
            Await DownloadFileHistoryRange(stageFilePageUrl, saveDir, $"{roleName}{stageName}", roleName, fromIdx, toIdx)
        Next
    End Function

    Private Async Function DownloadFileHistoryRange(filePageUrl As String, saveDir As String, baseFileName As String, roleName As String, fromIdx As Integer, toIdx As Integer) As Task
        Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
        Dim doc As New HtmlDocument()
        doc.LoadHtml(html)
        Dim historyDiv = doc.DocumentNode.SelectSingleNode("//div[@id='mw-imagepage-section-filehistory']")
        If historyDiv Is Nothing Then Return
        Dim table = historyDiv.SelectSingleNode(".//table[contains(@class,'filehistory')]")
        If table Is Nothing Then Return
        Dim rows = table.SelectNodes(".//tr[td]")
        If rows Is Nothing Then Return
        Dim historyDir = Path.Combine(saveDir, "历史版本")
        Directory.CreateDirectory(historyDir)
        Dim idx As Integer = 0
        For Each row In rows
            idx += 1
            If idx <= fromIdx Then Continue For
            If idx > toIdx Then Exit For
            Dim tdNodes = row.SelectNodes(".//td")
            If tdNodes Is Nothing OrElse tdNodes.Count < 2 Then Continue For
            Dim aNode = tdNodes(1).SelectSingleNode(".//a")
            If aNode Is Nothing Then Continue For
            Dim imgUrl = aNode.GetAttributeValue("href", "")
            If Not imgUrl.StartsWith("https://media.fgo.wiki/") Then Continue For
            If Not (imgUrl.ToLower().EndsWith(".png") OrElse imgUrl.ToLower().EndsWith(".jpg") OrElse imgUrl.ToLower().EndsWith(".jpeg") OrElse imgUrl.ToLower().EndsWith(".gif")) Then Continue For
            Dim ext = Path.GetExtension(imgUrl)
            Dim saveName = $"{baseFileName}_历史{idx}{ext}"
            Dim savePath = Path.Combine(historyDir, saveName)
            Try
                Using handler As New HttpClientHandler()
                    handler.AllowAutoRedirect = True
                    Using client As New HttpClient(handler)
                        Dim request = New HttpRequestMessage(HttpMethod.Get, imgUrl)
                        request.Headers.Referrer = New Uri(filePageUrl)
                        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36")
                        Dim response = Await client.SendAsync(request)
                        response.EnsureSuccessStatusCode()
                        Dim imgBytes = Await response.Content.ReadAsByteArrayAsync()
                        File.WriteAllBytes(savePath, imgBytes)
                        ListBox1.Items.Add($"  增量下载历史版本: {saveName}")
                        ListBox1.TopIndex = ListBox1.Items.Count - 1
                    End Using
                End Using
                Await Task.Delay(500)
            Catch ex As Exception
                ListBox1.Items.Add($"  [跳过历史] {saveName} ({imgUrl}): {ex.Message}")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                failIndex += 1
                WriteFailLog(failIndex, roleName, imgUrl)
            End Try
        Next
    End Function


    Private Async Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click
        Button1.Enabled = False
        Button2.Enabled = False
        Button3.Enabled = False
        Button4.Enabled = False
        ListBox1.Items.Clear()
        ListBox1.Items.Add("正在检查礼装增量更新...")
        Dim updateIniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.ini")
        Dim updateData = ReadUpdateIni(updateIniPath)
        Dim ceNames = Await GetAllCENameFromWiki()
        Dim updated As Boolean = False
        Dim rootPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images\礼装图鉴")
        Directory.CreateDirectory(rootPath)

        For Each ceName In ceNames
            Dim safeCEName = ceName
            Dim detailPageUrl = $"https://fgo.wiki/w/{safeCEName}"
            Dim ceDir = rootPath
            ' 解析各阶段历史图片数量
            Dim stageHistoryCount = Await GetGraphpickerHistoryCount(detailPageUrl)
            If Not updateData.ContainsKey(safeCEName) Then
                updateData(safeCEName) = New Dictionary(Of String, Integer)
            End If

            Dim needUpdate As Boolean = False
            For Each stage In stageHistoryCount.Keys
                Dim newCount = stageHistoryCount(stage)
                Dim oldCount = If(updateData(safeCEName).ContainsKey(stage), updateData(safeCEName)(stage), 0)
                If newCount > oldCount Then
                    needUpdate = True
                    ' 下载新增历史图片
                    Await DownloadGraphpickerHistoryStage(detailPageUrl, ceDir, safeCEName, stage, oldCount + 1, newCount)
                    updateData(safeCEName)(stage) = newCount
                End If
            Next

            If needUpdate Then
                ListBox1.Items.Add($"[更新] {safeCEName} 有新历史图片，已下载并记录。")
                updated = True
            End If
        Next

        If updated Then
            WriteUpdateIni(updateIniPath, updateData)
            ListBox1.Items.Add("update.ini 已更新。")
        Else
            ListBox1.Items.Add("无增量更新。")
        End If
        Button1.Enabled = True
        Button2.Enabled = True
        Button3.Enabled = True
        Button4.Enabled = True
    End Sub
    ' 下载角色所有阶段的历史图片
    Private Async Function DownloadAllGraphpickerHistoryImages(detailPageUrl As String, saveDir As String, roleName As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim graphDiv = doc.DocumentNode.SelectSingleNode("//div[@class='graphpicker']")
            If graphDiv Is Nothing Then
                ListBox1.Items.Add("[调试] 没有找到 graphpicker")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Return
            End If

            Dim aNodes = graphDiv.SelectNodes(".//a[@class='image']")
            If aNodes Is Nothing Then
                ListBox1.Items.Add("[调试] 没有找到立绘链接")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
                Return
            End If

            For Each aNode In aNodes
                Dim href = aNode.GetAttributeValue("href", "")
                If String.IsNullOrEmpty(href) Then Continue For
                Dim stageFilePageUrl As String
                If href.StartsWith("http") Then
                    stageFilePageUrl = href
                Else
                    stageFilePageUrl = "https://fgo.wiki" & href
                End If

                ' 获取阶段名
                Dim imgNode = aNode.SelectSingleNode(".//img")
                Dim stageName = ""
                If imgNode IsNot Nothing Then
                    stageName = Path.GetFileNameWithoutExtension(imgNode.GetAttributeValue("alt", ""))
                End If
                If String.IsNullOrEmpty(stageName) Then
                    stageName = "未知阶段"
                End If

                ' 下载该阶段的所有历史图片
                Await DownloadAllFileHistoryImages(stageFilePageUrl, saveDir, $"{roleName}{stageName}", roleName)
            Next
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] graphpicker历史: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function

    Private Async Function GetAllCEInfoFromWiki() As Task(Of List(Of ItemInfo))
        Dim url As String = "https://fgo.wiki/w/%E7%A4%BC%E8%A3%85%E5%9B%BE%E9%89%B4"
        Dim html As String = Await httpClient.GetStringAsync(url)
        Dim ceList As New List(Of ItemInfo)

        Dim m = Regex.Match(html, "override_data\s*=\s*""([\s\S]*?)"";")
        If Not m.Success Then Return ceList
        Dim data = m.Groups(1).Value.Replace("\n", vbLf)

        Dim idMatches = Regex.Matches(data, "id=(.+)")
        Dim nameMatches = Regex.Matches(data, "name=(.+)")
        Dim linkMatches = Regex.Matches(data, "name_link=(.+)")

        Dim count = Math.Min(idMatches.Count, Math.Min(nameMatches.Count, linkMatches.Count))
        For i = 0 To count - 1
            Dim info As New ItemInfo With {
                .Id = idMatches(i).Groups(1).Value.Trim(),
                .Name = nameMatches(i).Groups(1).Value.Trim(),
                .NameLink = linkMatches(i).Groups(1).Value.Trim()
            }
            ceList.Add(info)
        Next
        Return ceList
    End Function

    Private Async Function GetAllRoleInfoFromWiki() As Task(Of List(Of ItemInfo))
        Dim url As String = "https://fgo.wiki/w/%E8%8B%B1%E7%81%B5%E5%9B%BE%E9%89%B4"
        Dim html As String = Await httpClient.GetStringAsync(url)
        Dim roleList As New List(Of ItemInfo)

        ' 提取 override_data
        Dim m = Regex.Match(html, "override_data\s*=\s*""([\s\S]*?)"";")
        If Not m.Success Then Return roleList
        Dim data = m.Groups(1).Value.Replace("\n", vbLf)

        ' 按双换行分割每个角色
        Dim blocks = data.Split(New String() {vbLf & vbLf}, StringSplitOptions.RemoveEmptyEntries)
        For Each block In blocks
            Dim id = Regex.Match(block, "id=(.+)").Groups(1).Value.Trim()
            Dim name = Regex.Match(block, "name_cn=(.+)").Groups(1).Value.Trim()
            Dim nameLink = Regex.Match(block, "name_link=(.+)").Groups(1).Value.Trim()
            If Not String.IsNullOrEmpty(id) AndAlso Not String.IsNullOrEmpty(name) AndAlso Not String.IsNullOrEmpty(nameLink) Then
                roleList.Add(New ItemInfo With {.Id = id, .Name = name, .NameLink = nameLink})
            End If
        Next
        Return roleList
    End Function

    Private Async Function DownloadCEOriginalImage(detailPageUrl As String, savePath As String) As Task
        Try
            If File.Exists(savePath) Then Return
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            Dim aNode = doc.DocumentNode.SelectSingleNode("//td[contains(@class,'概念礼装_图片')]//a[@class='image']")
            If aNode Is Nothing Then Return
            Dim filePageUrl = "https://fgo.wiki" & aNode.GetAttributeValue("href", "")
            Dim fileHtml As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim fileDoc As New HtmlDocument()
            fileDoc.LoadHtml(fileHtml)
            Dim imgNode = fileDoc.DocumentNode.SelectSingleNode("//div[@class='fullImageLink' and @id='file']/a")
            If imgNode Is Nothing Then Return
            Dim imgUrl = imgNode.GetAttributeValue("href", "")
            If Not imgUrl.StartsWith("http") Then imgUrl = "https:" & imgUrl
            Dim imgBytes = Await httpClient.GetByteArrayAsync(imgUrl)
            File.WriteAllBytes(savePath, imgBytes)
        Catch ex As Exception
            ' 错误处理
        End Try
    End Function

    ' 保存概念礼装解说文本
    Private Async Function SaveCECommentary(detailPageUrl As String, id As String, name As String, saveDir As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            ' 查找解说div
            Dim commentaryDiv = doc.DocumentNode.SelectSingleNode("//div[starts-with(@class,'概念礼装_日文解说_')]")
            If commentaryDiv Is Nothing Then Return

            ' 查找poem段
            Dim poemDiv = commentaryDiv.SelectSingleNode(".//div[@class='poem']")
            If poemDiv Is Nothing Then Return

            ' 提取纯文本，保留换行
            Dim textBuilder As New System.Text.StringBuilder()
            For Each node In poemDiv.ChildNodes
                If node.Name = "p" OrElse node.Name = "#text" Then
                    textBuilder.Append(HtmlToPlainText(node.InnerHtml))
                End If
            Next

            Dim commentaryText = textBuilder.ToString().Trim()
            If String.IsNullOrWhiteSpace(commentaryText) Then Return

            ' 保存到解说目录
            Dim commentaryDir = Path.Combine(saveDir, "解说")
            Directory.CreateDirectory(commentaryDir)
            Dim fileName = $"{id.PadLeft(4, "0"c)}.{name}.txt"
            Dim filePath = Path.Combine(commentaryDir, fileName)
            File.WriteAllText(filePath, commentaryText, System.Text.Encoding.UTF8)
        Catch ex As Exception
            ' 可选：错误处理
        End Try
    End Function

    ' HTML转纯文本，保留换行
    Private Function HtmlToPlainText(html As String) As String
        Dim temp = html.Replace("<br>", vbCrLf).Replace("<br/>", vbCrLf).Replace("<br />", vbCrLf)
        temp = Regex.Replace(temp, "<.*?>", "") ' 去除所有HTML标签
        temp = System.Web.HttpUtility.HtmlDecode(temp)
        Return temp
    End Function

    ' 下载角色各阶段图标与战斗形象图片（含历史版本）
    Private Async Function DownloadRoleIconsAndBattleImages(detailPageUrl As String, roleDir As String, roleName As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim iconDir = Path.Combine(roleDir, "各阶段图标")
            Directory.CreateDirectory(iconDir)
            Dim iconHistoryDir = Path.Combine(iconDir, "历史")
            Directory.CreateDirectory(iconHistoryDir)

            Dim battleDir = Path.Combine(roleDir, "战斗形象")
            Directory.CreateDirectory(battleDir)
            Dim battleHistoryDir = Path.Combine(battleDir, "历史")
            Directory.CreateDirectory(battleHistoryDir)

            ' 1. 各阶段图标（再临阶段图标）
            Dim iconTable = doc.DocumentNode.SelectSingleNode("//h3[span[@id='再临阶段图标']]/following-sibling::table[1]")
            If iconTable IsNot Nothing Then
                Dim aNodes = iconTable.SelectNodes(".//a[@class='image']")
                Dim iconIdx As Integer = 0
                If aNodes IsNot Nothing Then
                    For Each aNode In aNodes
                        Dim fileHref = aNode.GetAttributeValue("href", "")
                        If String.IsNullOrEmpty(fileHref) OrElse Not fileHref.StartsWith("/w/") Then Continue For
                        iconIdx += 1
                        Dim filePageUrl = "https://fgo.wiki" & fileHref
                        Dim baseFileName = $"{roleName}_图标{iconIdx}"
                        Await DownloadRoleIconMainImage(filePageUrl, iconDir, baseFileName, roleName)
                        Await DownloadRoleIconHistoryImages(filePageUrl, iconHistoryDir, baseFileName, roleName)
                    Next
                End If
                If iconIdx = 0 Then
                    ListBox1.Items.Add("未找到任何再临阶段图标图片链接")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                End If
            Else
                ListBox1.Items.Add("未找到再临阶段图标表格")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
            End If

            ' 2. 战斗形象
            Dim battleTable = doc.DocumentNode.SelectSingleNode("//h3[span[@id='战斗形象']]/following-sibling::table[1]")
            If battleTable IsNot Nothing Then
                Dim aNodes = battleTable.SelectNodes(".//a[@class='image']")
                Dim battleIdx As Integer = 0
                If aNodes IsNot Nothing Then
                    For Each aNode In aNodes
                        Dim fileHref = aNode.GetAttributeValue("href", "")
                        If String.IsNullOrEmpty(fileHref) OrElse Not fileHref.StartsWith("/w/") Then Continue For
                        battleIdx += 1
                        Dim filePageUrl = "https://fgo.wiki" & fileHref
                        Dim baseFileName = $"{roleName}_战斗形象{battleIdx}"
                        Await DownloadRoleIconMainImage(filePageUrl, battleDir, baseFileName, roleName)
                        Await DownloadRoleIconHistoryImages(filePageUrl, battleHistoryDir, baseFileName, roleName)
                    Next
                End If
                If battleIdx = 0 Then
                    ListBox1.Items.Add("未找到任何战斗形象图片链接")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                End If
            Else
                ListBox1.Items.Add("未找到战斗形象表格")
                ListBox1.TopIndex = ListBox1.Items.Count - 1
            End If
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 图标与战斗形象: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function

    ' 下载主图（当前版本）
    Private Async Function DownloadRoleIconMainImage(filePageUrl As String, saveDir As String, baseFileName As String, roleKey As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            Dim table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'filehistory')]")
            If table Is Nothing Then Return
            Dim row = table.SelectSingleNode(".//tr[td]") ' 第一行为当前
            If row Is Nothing Then Return
            Dim tdNodes = row.SelectNodes(".//td")
            If tdNodes Is Nothing OrElse tdNodes.Count < 3 Then Return
            Dim aNode = tdNodes(2).SelectSingleNode(".//a")
            If aNode Is Nothing Then Return
            Dim imgUrl = aNode.GetAttributeValue("href", "")
            If Not imgUrl.StartsWith("https://media.fgo.wiki/") Then Return
            Dim ext = Path.GetExtension(imgUrl)
            Dim savePath = Path.Combine(saveDir, $"{baseFileName}{ext}")
            Dim imgBytes = Await httpClient.GetByteArrayAsync(imgUrl)
            File.WriteAllBytes(savePath, imgBytes)
            ListBox1.Items.Add($"  下载图标主图: {baseFileName}{ext}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过主图] {baseFileName}: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function

    ' 下载历史图片
    Private Async Function DownloadRoleIconHistoryImages(filePageUrl As String, saveDir As String, baseFileName As String, roleKey As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(filePageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)
            Dim table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'filehistory')]")
            If table Is Nothing Then Return
            Dim rows = table.SelectNodes(".//tr[td]")
            If rows Is Nothing Then Return
            Dim idx As Integer = 0
            For Each row In rows
                idx += 1
                Dim tdNodes = row.SelectNodes(".//td")
                If tdNodes Is Nothing OrElse tdNodes.Count < 3 Then Continue For
                Dim aNode = tdNodes(2).SelectSingleNode(".//a")
                If aNode Is Nothing Then Continue For
                Dim imgUrl = aNode.GetAttributeValue("href", "")
                If Not imgUrl.StartsWith("https://media.fgo.wiki/") Then Continue For
                Dim ext = Path.GetExtension(imgUrl)
                Dim saveName = $"{baseFileName}_历史{idx}{ext}"
                Dim savePath = Path.Combine(saveDir, saveName)
                Try
                    Dim imgBytes = Await httpClient.GetByteArrayAsync(imgUrl)
                    File.WriteAllBytes(savePath, imgBytes)
                    ListBox1.Items.Add($"  下载图标历史: {saveName}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                Catch ex As Exception
                    ListBox1.Items.Add($"  [跳过历史] {saveName}: {ex.Message}")
                    ListBox1.TopIndex = ListBox1.Items.Count - 1
                End Try
            Next
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过历史] {baseFileName}: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function

    ' 下载并保存角色资料到资料.txt
    Private Async Function SaveRoleProfile(detailPageUrl As String, roleDir As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlDocument()
            doc.LoadHtml(html)

            Dim sb As New System.Text.StringBuilder()

            ' 1. 基础资料表格（保留原有角色详情内容）
            Dim baseTable = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'wikitable') and contains(@class,'graphpicker-container')]")
            If baseTable IsNot Nothing Then
                Dim trs = baseTable.SelectNodes("./tbody/tr")
                If trs IsNot Nothing AndAlso trs.Count >= 7 Then
                    sb.AppendLine("===============角色详情==============")
                    Dim nameCn = trs(0).SelectSingleNode(".//th")?.InnerText.Trim()
                    Dim nameJp = trs(1).SelectSingleNode(".//td")?.InnerText.Trim()
                    Dim nameEn = trs(2).SelectSingleNode(".//td")?.InnerText.Trim()
                    sb.AppendLine($"中文：{nameCn}")
                    sb.AppendLine($"日文：{nameJp}")
                    sb.AppendLine($"英文：{nameEn}")
                    sb.AppendLine()
                    Dim tds = trs(4).SelectNodes("./td")
                    Dim artist As String = ""
                    Dim cv As String = ""
                    If tds IsNot Nothing AndAlso tds.Count >= 2 Then
                        ' 新增：只取第一个可见span里的a
                        Dim artistTd = tds(0)
                        If artistTd IsNot Nothing Then
                            Dim artistSpan = artistTd.SelectSingleNode(".//span[contains(@style,'display:inline')]")
                            If artistSpan IsNot Nothing Then
                                Dim artistA = artistSpan.SelectSingleNode(".//a")
                                If artistA IsNot Nothing Then
                                    artist = artistA.InnerText.Trim()
                                End If
                            End If
                            If String.IsNullOrEmpty(artist) Then
                                artist = artistTd.InnerText.Trim()
                            End If
                        End If
                        cv = tds(1).InnerText.Trim()
                    End If
                    sb.AppendLine($"画师：{artist}")
                    sb.AppendLine($"声优：{cv}")
                    sb.AppendLine()
                    Dim job = trs(6).SelectSingleNode(".//td[1]")?.InnerText.Trim()
                    Dim gender = trs(6).SelectSingleNode(".//td[2]")?.InnerText.Trim()
                    Dim height = trs(6).SelectSingleNode(".//td[3]")?.InnerText.Trim()
                    Dim weight = trs(6).SelectSingleNode(".//td[4]")?.InnerText.Trim()
                    Dim attr = trs(6).SelectSingleNode(".//td[5]")?.InnerText.Trim()
                    Dim subAttr = trs(6).SelectSingleNode(".//td[6]")?.InnerText.Trim()
                    sb.AppendLine($"职阶：{job}")
                    sb.AppendLine($"性别：{gender}")
                    sb.AppendLine($"身高：{height}")
                    sb.AppendLine($"体重：{weight}")
                    sb.AppendLine($"属性：{attr}")
                    sb.AppendLine($"副属性：{subAttr}")
                    sb.AppendLine()
                End If
            End If

            sb.AppendLine("===============角色资料==============")
            ' 2. 只采集 <div id="ooui-svt-profile-option-container-1"> 之后的资料内容
            Dim startDiv = doc.DocumentNode.SelectSingleNode("//div[@id='ooui-svt-profile-option-container-1']")
            If startDiv Is Nothing Then
                'ListBox1.Items.Add("[调试] 未找到 ooui-svt-profile-option-container-1 节点")
            Else
                'ListBox1.Items.Add("[调试] 找到 ooui-svt-profile-option-container-1 节点")
                Dim node = startDiv.NextSibling
                Dim foundTabber As Boolean = False
                While node IsNot Nothing
                    If node.Name = "h2" Then Exit While
                    If node.NodeType = HtmlAgilityPack.HtmlNodeType.Element AndAlso node.Name = "div" AndAlso node.GetAttributeValue("class", "").Contains("tabber") Then
                        foundTabber = True
                        'ListBox1.Items.Add("[调试] 找到tabber节点")
                        Dim articles = node.SelectNodes(".//article[contains(@class,'tabber__panel')]")
                        If articles Is Nothing Then
                            ' ListBox1.Items.Add("[调试] 未找到任何article节点")
                        Else
                            'ListBox1.Items.Add($"[调试] 找到{articles.Count}个article节点")
                            For Each art In articles
                                Dim title As String = art.GetAttributeValue("data-title", "").Trim()
                                'ListBox1.Items.Add($"[调试] article data-title: {title}")
                                If String.IsNullOrEmpty(title) Then Continue For
                                If title = "角色详情" OrElse title.StartsWith("个人资料") Then
                                    sb.AppendLine($"【{title}】")
                                    Dim table = art.SelectSingleNode(".//table")
                                    If table Is Nothing Then
                                        'ListBox1.Items.Add($"[调试] 【{title}】未找到table节点")
                                    Else
                                        'ListBox1.Items.Add($"[调试] 【{title}】找到table节点")
                                        ' 小标题
                                        Dim ths = table.SelectNodes(".//th")
                                        If ths IsNot Nothing Then
                                            For Each th In ths
                                                Dim thText = HtmlToPlainText(th.InnerText).Trim()
                                                If thText = "角色详情" Then Continue For
                                                If Not String.IsNullOrWhiteSpace(thText) Then
                                                    sb.AppendLine(thText)
                                                End If
                                            Next
                                        End If
                                        ' 中文资料
                                        Dim cnDiv = table.SelectSingleNode(".//div[contains(@class,'tl_svt_profile_cn_1')]")
                                        If cnDiv Is Nothing Then
                                            'ListBox1.Items.Add($"[调试] 【{title}】未找到中文资料div")
                                        Else
                                            Dim ps = cnDiv.SelectNodes(".//p")
                                            If ps IsNot Nothing Then
                                                For Each p In ps
                                                    Dim line = HtmlToPlainText(p.InnerHtml).Trim()
                                                    If Not String.IsNullOrWhiteSpace(line) Then
                                                        sb.AppendLine(line)
                                                    End If
                                                Next
                                            End If
                                        End If
                                        sb.AppendLine("--------------------")
                                        ' 日文资料
                                        Dim jpDiv = table.SelectSingleNode(".//div[contains(@class,'tl_svt_profile_jp_1')]")
                                        If jpDiv Is Nothing Then
                                            'ListBox1.Items.Add($"[调试] 【{title}】未找到日文资料div")
                                        Else
                                            Dim ps = jpDiv.SelectNodes(".//p")
                                            If ps IsNot Nothing Then
                                                For Each p In ps
                                                    Dim line = HtmlToPlainText(p.InnerHtml).Trim()
                                                    If Not String.IsNullOrWhiteSpace(line) Then
                                                        sb.AppendLine(line)
                                                    End If
                                                Next
                                            End If
                                        End If
                                    End If
                                    sb.AppendLine()
                                End If
                            Next
                        End If
                    End If
                    node = node.NextSibling
                End While
                If Not foundTabber Then
                    'ListBox1.Items.Add("[调试] 未找到tabber节点（div class=tabber）")
                End If
            End If

            Dim filePath = Path.Combine(roleDir, "资料.txt")
            Dim resultText = Regex.Replace(sb.ToString().Trim(), "(\r?\n){3,}", vbCrLf & vbCrLf)
            File.WriteAllText(filePath, resultText, System.Text.Encoding.UTF8)
            ListBox1.Items.Add($"  资料已保存：{filePath}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 资料保存失败: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function

    '下载角色语音
    Private Async Function DownloadRoleVoice(detailPageUrl As String, roleDir As String) As Task
        Try
            Dim html As String = Await httpClient.GetStringAsync(detailPageUrl)
            Dim doc As New HtmlAgilityPack.HtmlDocument()
            doc.LoadHtml(html)

            ' 1. 找到“语音”h2节点
            Dim voiceH2 = doc.DocumentNode.SelectSingleNode("//h2[span[@id='语音'] or span[@id='.E8.AF.AD.E9.9F.B3'] or span[@class='mw-headline' and (text()='语音')]]")
            If voiceH2 Is Nothing Then Return

            ' 2. 获取“语音”h2之后的所有wikitable
            Dim tables As New List(Of HtmlNode)
            Dim node = voiceH2.NextSibling
            While node IsNot Nothing
                If node.NodeType = HtmlAgilityPack.HtmlNodeType.Element Then
                    If node.Name = "h2" Then Exit While
                    If node.Name = "table" AndAlso node.GetAttributeValue("class", "").Contains("wikitable") Then
                        tables.Add(node)
                    End If
                End If
                node = node.NextSibling
            End While
            If tables.Count = 0 Then Return

            For Each table In tables
                ' 获取分类名（如“战斗”“召唤和强化”等）
                Dim th = table.SelectSingleNode(".//tr/th[contains(@colspan,'2') or contains(@colspan,'3')]")
                If th Is Nothing Then Continue For
                Dim category = th.InnerText.Trim()
                If String.IsNullOrWhiteSpace(category) Then Continue For

                ' 目录名：角色\语音\分类
                Dim categoryDir = Path.Combine(roleDir, "语音", category)
                Directory.CreateDirectory(categoryDir)
                Dim txtPath = Path.Combine(categoryDir, "语音.txt")
                Dim txtSb As New System.Text.StringBuilder()

                ' 遍历tr
                Dim trs = table.SelectNodes(".//tr[th and (td or th)]")
                If trs Is Nothing Then Continue For

                Dim i As Integer = 1
                While i <= trs.Count - 1
                    Dim tr = trs(i)
                    Dim thNode = tr.SelectSingleNode("./th")
                    If thNode Is Nothing Then
                        i += 1
                        Continue While
                    End If
                    Dim title As String = thNode.InnerText.Trim()

                    ' 查找语音文本
                    Dim textNode = tr.SelectSingleNode("./td/p")
                    If textNode Is Nothing Then
                        If i + 1 < trs.Count Then
                            Dim nextTr = trs(i + 1)
                            Dim textTd = nextTr.SelectSingleNode("./td")
                            If textTd Is Nothing Then
                                textTd = nextTr.SelectSingleNode("./th")
                            End If
                            If textTd IsNot Nothing Then
                                textNode = textTd.SelectSingleNode("./p")
                                i += 1
                            End If
                        End If
                    End If
                    Dim text As String = ""
                    If textNode IsNot Nothing Then
                        text = HtmlToPlainText(textNode.InnerHtml).Trim()
                    End If

                    ' 查找语音url
                    Dim audioNode = tr.SelectSingleNode(".//span[contains(@class,'MiniAudioPlayer')]//audio/source")
                    If audioNode Is Nothing Then
                        audioNode = thNode.SelectSingleNode(".//span[contains(@class,'MiniAudioPlayer')]//audio/source")
                    End If
                    Dim audioUrl As String = ""
                    If audioNode IsNot Nothing Then
                        audioUrl = audioNode.GetAttributeValue("src", "")
                    End If
                    If Not String.IsNullOrWhiteSpace(audioUrl) Then
                        If audioUrl.StartsWith("//") Then audioUrl = "https:" & audioUrl
                        Dim ext = Path.GetExtension(audioUrl)
                        Dim safeTitle = MakeSafeFileName(title)

                        Dim savePath = Path.Combine(categoryDir, safeTitle & ext)
                        If Not File.Exists(savePath) Then
                            Try
                                Dim bytes = Await httpClient.GetByteArrayAsync(audioUrl)
                                File.WriteAllBytes(savePath, bytes)
                                ListBox1.Items.Add($"  下载语音: 语音/{category}/{safeTitle}{ext}")
                                ListBox1.TopIndex = ListBox1.Items.Count - 1
                            Catch ex As Exception
                                ListBox1.Items.Add($"  [跳过语音] 语音/{category}/{safeTitle}: {ex.Message}")
                                ListBox1.TopIndex = ListBox1.Items.Count - 1
                            End Try
                        End If
                    End If

                    ' 写入文本
                    If Not String.IsNullOrWhiteSpace(text) Then
                        txtSb.AppendLine($"{title}：{text}")
                    End If
                    i += 1
                End While

                ' 写入语音.txt
                If txtSb.Length > 0 Then
                    File.WriteAllText(txtPath, txtSb.ToString().Trim(), System.Text.Encoding.UTF8)
                End If
            Next
        Catch ex As Exception
            ListBox1.Items.Add($"  [跳过] 语音下载失败: {ex.Message}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1
        End Try
    End Function


    ' 生成安全的文件名，替换所有非法字符为下划线
    Private Function MakeSafeFileName(input As String) As String
        ' 替换 \ / : * ? " < > | 为 _
        Return Regex.Replace(input, "[\\/:*?""<>|]", "_")
    End Function

    Private Async Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Button1.Enabled = False
        Button2.Enabled = False
        Button3.Enabled = False
        Button4.Enabled = False
        Button5.Enabled = False
        ListBox1.Items.Clear()
        ListBox1.Items.Add("批量采集角色ID 1-441 ...")
        ListBox1.TopIndex = ListBox1.Items.Count - 1

        ' 获取全部角色信息
        Dim roleList = Await GetAllRoleInfoFromWiki()
        Dim testIds = Enumerable.Range(1, 441).Select(Function(i) i.ToString()).ToArray()
        Dim rootPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images\角色")

        For Each testId In testIds
            Dim info = roleList.FirstOrDefault(Function(r) r.Id = testId)
            If String.IsNullOrEmpty(info.Id) Then
                ListBox1.Items.Add($"未找到ID={testId}的角色")
                Continue For
            End If
            Dim dirName = $"{info.Id.PadLeft(4, "0"c)}.{info.Name}"
            Dim roleDir = Path.Combine(rootPath, dirName)
            Directory.CreateDirectory(roleDir)
            Dim detailPageUrl = $"https://fgo.wiki/w/{info.NameLink}"
            ListBox1.Items.Add($"正在采集：{dirName}")
            ListBox1.TopIndex = ListBox1.Items.Count - 1

            Await SaveRoleProfile(detailPageUrl, roleDir)
        Next

        ListBox1.Items.Add("批量采集完成。请检查资料.txt 内容。")
        ListBox1.TopIndex = ListBox1.Items.Count - 1
        Button1.Enabled = True
        Button2.Enabled = True
        Button3.Enabled = True
        Button4.Enabled = True
        Button5.Enabled = True
    End Sub
End Class
