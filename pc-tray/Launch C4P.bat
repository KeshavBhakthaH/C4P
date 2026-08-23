@echo off
setlocal
title C4P launcher

rem Detect the .NET 8 Desktop Runtime.
set "RUNTIME_FOUND="
where dotnet >nul 2>nul && (
    for /f "usebackq delims=" %%i in (`dotnet --list-runtimes 2^>nul ^| findstr /c:"Microsoft.WindowsDesktop.App 8"`) do set "RUNTIME_FOUND=1"
)

if defined RUNTIME_FOUND (
    echo .NET 8 Desktop Runtime found - starting C4P...
    start "" "%~dp0C4P.exe"
    exit /b 0
)

echo.
echo   C4P needs the .NET 8 Desktop Runtime (one-time install, ~55 MB).
echo   It was not found on this PC.
echo.
choice /c YN /n /m "Download and install it now from Microsoft? [Y/N] "
if errorlevel 2 exit /b 1

set "INSTALLER=%TEMP%\windowsdesktop-runtime-win-x64.exe"
echo Downloading installer from Microsoft...
curl -L --fail --progress-bar -o "%INSTALLER%" "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
if errorlevel 1 goto :download_failed

echo.
echo Running Microsoft's installer - accept the UAC prompt if it appears...
"%INSTALLER%" /passive /norestart
set "INSTALL_EXIT=%errorlevel%"
del "%INSTALLER%" >nul 2>nul

rem Re-check after installation.
set "RUNTIME_FOUND="
for /f "usebackq delims=" %%i in (`dotnet --list-runtimes 2^>nul ^| findstr /c:"Microsoft.WindowsDesktop.App 8"`) do set "RUNTIME_FOUND=1"

if not defined RUNTIME_FOUND (
    echo.
    echo The runtime still was not detected (installer exit code %INSTALL_EXIT%).
    echo You can download it manually at:
    echo   https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    pause
    exit /b 1
)

echo Done - starting C4P...
start "" "%~dp0C4P.exe"
exit /b 0

:download_failed
echo.
echo Download failed. Opening the download page instead...
start "" "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
pause
exit /b 1
