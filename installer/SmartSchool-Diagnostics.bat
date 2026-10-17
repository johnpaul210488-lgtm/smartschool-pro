@echo off
title SmartSchool Diagnostics
echo =======================================================
echo              SmartSchool DIAGNOSTICS
echo  (Is window ka SCREENSHOT developer ko bhej dein)
echo =======================================================
echo.
echo --- 1. SQL LocalDB installed? (koi bhi version):
set LDB=
if exist "%ProgramFiles%\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe" set LDB=%ProgramFiles%\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe
if exist "%ProgramFiles%\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe" set LDB=%ProgramFiles%\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe
if exist "%ProgramFiles(X86)%\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe" set LDB=%ProgramFiles(X86)%\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe
if exist "%ProgramFiles(X86)%\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe" set LDB=%ProgramFiles(X86)%\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe
if defined LDB (echo   %LDB%) else (echo   !! MISSING - LocalDB install nahi hui)
echo.
echo --- 2. LocalDB instances:
if defined LDB "%LDB%" info
echo.
echo --- 3. API process chal rahi hai?:
tasklist /fi "imagename eq SmartSchool.API.exe" | findstr /i SmartSchool.API.exe
echo.
echo --- 4. Port 5000 par koi sun raha hai?:
netstat -ano | findstr :5000
echo.
echo --- 5. API log (aakhri 25 lines — YEH sabse kaam ki hai):
powershell -command "Get-Content 'C:\ProgramData\SmartSchool\logs\smartschool-*.log' -Tail 25 -ErrorAction SilentlyContinue"
echo.
echo =======================================================
pause
