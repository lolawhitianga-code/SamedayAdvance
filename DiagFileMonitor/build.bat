@echo off
setlocal

echo ==========================================================
echo   Diagnostic File Monitor - build
echo ==========================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto nodotnet

echo Found .NET version:
dotnet --version
echo.
echo Building. The first run downloads a few things and can take
echo several minutes. Later runs are much quicker.
echo.

dotnet publish "%~dp0src\DiagFileMonitor.App\DiagFileMonitor.App.csproj" ^
    -c Release -r win-x64 --self-contained true -o "%~dp0publish"

if errorlevel 1 goto failed

echo.
echo ==========================================================
echo   Done.
echo.
echo   Your program is here:
echo     %~dp0publish\DiagFileMonitor.exe
echo.
echo   Double-click that file to run it.
echo ==========================================================
echo.
pause
exit /b 0

:nodotnet
echo .NET is not installed on this PC yet.
echo.
echo 1. Go to:  https://dotnet.microsoft.com/download/dotnet/8.0
echo 2. Under ".NET 8.0", in the SDK column, download the
echo    Windows x64 installer.
echo 3. Run the installer, then close and reopen this window
echo    and double-click build.bat again.
echo.
pause
exit /b 1

:failed
echo.
echo ==========================================================
echo   BUILD FAILED
echo.
echo   Scroll up to find the first line containing "error".
echo   Copy that line and send it to me and I will sort it.
echo ==========================================================
echo.
pause
exit /b 1
