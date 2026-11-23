@echo off
@rem 本文件必须保存为 ANSI(GBK) 编码：cmd 按系统代码页读取，UTF-8 会乱码。
setlocal
cd /d "%~dp0"

echo ============================================
echo   HanVGamepad 启动
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 找不到 dotnet，请先安装 .NET 10 SDK 并加入 PATH。
    pause
    exit /b 1
)

if not exist "src\desktop\wwwroot\index.html" (
    echo [提示] 还没有 web 构建产物，页面会 404。
    echo        请先运行「构建web.bat」，或执行 npm run build。
    echo.
)

echo 正在启动桌面端（Ctrl+C 可停止）...
echo.
dotnet run --project "src\desktop\HanVGamepad.Desktop.csproj" -- %*

if errorlevel 1 (
    echo.
    echo [错误] 启动失败，请查看上面的输出。
    pause
    exit /b 1
)

endlocal
