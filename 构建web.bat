@echo off
@rem 本文件必须保存为 ANSI(GBK) 编码：cmd 按系统代码页读取，UTF-8 会乱码。
setlocal
cd /d "%~dp0web-client"

echo ============================================
echo   构建 web-client  -^>  src\desktop\wwwroot
echo ============================================
echo.

where npm >nul 2>nul
if errorlevel 1 (
    echo [错误] 找不到 npm，请先安装 Node.js 并加入 PATH。
    pause
    exit /b 1
)

if not exist "node_modules" (
    echo 首次构建，安装依赖（可能需要几分钟）...
    call npm install
    if errorlevel 1 goto fail
    echo.
)

echo 正在构建...
echo.
call npm run build
if errorlevel 1 goto fail

echo.
echo [完成] 产物已写入 src\desktop\wwwroot
echo        现在可以直接运行「启动.bat」。
echo.
pause
exit /b 0

:fail
echo.
echo [错误] 构建失败，请查看上面的输出。
echo.
pause
exit /b 1
