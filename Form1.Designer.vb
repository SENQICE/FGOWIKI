<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Form1
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Required by the Windows Form Designer
    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.  
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(Form1))
        Button1 = New Button()
        ProgressBar1 = New ProgressBar()
        ProgressBar2 = New ProgressBar()
        ListBox1 = New ListBox()
        Button2 = New Button()
        Label1 = New Label()
        ButtonPause = New Button()
        Button3 = New Button()
        Button4 = New Button()
        Button5 = New Button()
        SuspendLayout()
        ' 
        ' Button1
        ' 
        Button1.Location = New Point(481, 278)
        Button1.Name = "Button1"
        Button1.Size = New Size(75, 23)
        Button1.TabIndex = 0
        Button1.Text = "下载角色"
        Button1.UseVisualStyleBackColor = True
        ' 
        ' ProgressBar1
        ' 
        ProgressBar1.Location = New Point(12, 278)
        ProgressBar1.Name = "ProgressBar1"
        ProgressBar1.Size = New Size(444, 23)
        ProgressBar1.TabIndex = 1
        ' 
        ' ProgressBar2
        ' 
        ProgressBar2.Location = New Point(12, 314)
        ProgressBar2.Name = "ProgressBar2"
        ProgressBar2.Size = New Size(444, 23)
        ProgressBar2.TabIndex = 2
        ' 
        ' ListBox1
        ' 
        ListBox1.FormattingEnabled = True
        ListBox1.ItemHeight = 17
        ListBox1.Location = New Point(12, 12)
        ListBox1.Name = "ListBox1"
        ListBox1.Size = New Size(707, 242)
        ListBox1.TabIndex = 3
        ' 
        ' Button2
        ' 
        Button2.Location = New Point(562, 278)
        Button2.Name = "Button2"
        Button2.Size = New Size(75, 23)
        Button2.TabIndex = 4
        Button2.Text = "下载礼装"
        Button2.UseVisualStyleBackColor = True
        ' 
        ' Label1
        ' 
        Label1.AutoSize = True
        Label1.Location = New Point(668, 281)
        Label1.Name = "Label1"
        Label1.Size = New Size(23, 17)
        Label1.TabIndex = 5
        Label1.Text = "-/-"
        ' 
        ' ButtonPause
        ' 
        ButtonPause.Location = New Point(644, 314)
        ButtonPause.Name = "ButtonPause"
        ButtonPause.Size = New Size(75, 23)
        ButtonPause.TabIndex = 6
        ButtonPause.Text = "暂停"
        ButtonPause.UseVisualStyleBackColor = True
        ' 
        ' Button3
        ' 
        Button3.Enabled = False
        Button3.Location = New Point(481, 314)
        Button3.Name = "Button3"
        Button3.Size = New Size(75, 23)
        Button3.TabIndex = 7
        Button3.Text = "更新角色"
        Button3.UseVisualStyleBackColor = True
        ' 
        ' Button4
        ' 
        Button4.Enabled = False
        Button4.Location = New Point(562, 314)
        Button4.Name = "Button4"
        Button4.Size = New Size(75, 23)
        Button4.TabIndex = 8
        Button4.Text = "更新礼装"
        Button4.UseVisualStyleBackColor = True
        ' 
        ' Button5
        ' 
        Button5.Location = New Point(769, 278)
        Button5.Name = "Button5"
        Button5.Size = New Size(126, 23)
        Button5.TabIndex = 9
        Button5.Text = "下载角色资料"
        Button5.UseVisualStyleBackColor = True
        ' 
        ' Form1
        ' 
        AutoScaleDimensions = New SizeF(7F, 17F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(730, 349)
        Controls.Add(Button5)
        Controls.Add(Button4)
        Controls.Add(Button3)
        Controls.Add(ButtonPause)
        Controls.Add(Label1)
        Controls.Add(Button2)
        Controls.Add(ListBox1)
        Controls.Add(ProgressBar2)
        Controls.Add(ProgressBar1)
        Controls.Add(Button1)
        FormBorderStyle = FormBorderStyle.FixedSingle
        Icon = CType(resources.GetObject("$this.Icon"), Icon)
        MaximizeBox = False
        Name = "Form1"
        StartPosition = FormStartPosition.CenterScreen
        Text = "FGO Wiki 下载器 v1.0"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Button1 As Button
    Friend WithEvents ProgressBar1 As ProgressBar
    Friend WithEvents ProgressBar2 As ProgressBar
    Friend WithEvents ListBox1 As ListBox
    Friend WithEvents Button2 As Button
    Friend WithEvents Label1 As Label
    Friend WithEvents ButtonPause As Button
    Friend WithEvents Button3 As Button
    Friend WithEvents Button4 As Button
    Friend WithEvents Button5 As Button

End Class
