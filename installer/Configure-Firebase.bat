@echo off
setlocal
set "SMARTSCHOOL_FIREBASE_SETUP=%~f0"
:: Elevate before choosing or copying the key. The key is never passed on the command line.
powershell.exe -NoProfile -Command "$p = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()); if ($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }"
if errorlevel 1 (
    powershell.exe -NoProfile -Command "try { Start-Process -FilePath $env:SMARTSCHOOL_FIREBASE_SETUP -Verb RunAs } catch { Write-Host 'Administrator permission zaroori hai.'; exit 1 }"
    exit /b
)
title Queen School - Firebase Notification Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Configure-Firebase.ps1"
set "SETUP_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %SETUP_EXIT%
