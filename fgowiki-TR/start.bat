@echo off
chcp 65001 >nul
echo ========================================
echo    FGO Wiki 资源下载器
echo ========================================
echo.

:: 检查Python是否安装
python --version >nul 2>&1
if errorlevel 1 (
    echo 错误: 未检测到Python，请先安装Python 3.7或更高版本
    echo 下载地址: https://www.python.org/downloads/
    pause
    exit /b 1
)

:: 检查依赖是否安装
echo 正在检查依赖...
pip show requests >nul 2>&1
if errorlevel 1 (
    echo 发现缺少依赖，正在安装...
    pip install -r requirements.txt
)

echo.
echo 安装检查完成！
echo.

:: 显示启动选项
echo 请选择启动方式：
echo   1. 交互式界面（推荐新手）
echo   2. 下载所有英灵
echo   3. 下载所有概念礼装
echo   4. 下载所有资源
echo   5. 验证已下载文件
echo   0. 退出
echo.

set /p choice=请输入选项 (0-5):
echo.

if "%choice%"=="1" goto interactive
if "%choice%"=="2" goto servants
if "%choice%"=="3" goto ces
if "%choice%"=="4" goto all
if "%choice%"=="5" goto verify
if "%choice%"=="0" exit /b 0

echo 无效的选择
pause
exit /b 1

:interactive
python main.py
goto end

:servants
python main.py --servants
goto end

:ces
python main.py --ces
goto end

:all
python main.py --all
goto end

:verify
python main.py --verify
goto end

:end
pause
