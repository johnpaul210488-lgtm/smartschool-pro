; ============================================================================
; Queen International School — One-Click SCHOOL SERVER installer (Inno Setup)
;
; What it does, silently, in order:
;   1. Installs SQL Server 2022 LocalDB (bundled MSI)
;   2. Lays down the API (Kestrel exe — khud migrations+seed startup par)
;   3. Auto-starts API now + at every login (interactive user's own LocalDB)
;   4. Installs the WPF Admin app + Desktop shortcut + Diagnostics shortcut
;
; Built by CI (release.yml). To build by hand:
;   - dotnet publish src\SmartSchool.API -c Release -o build-artifacts\api
;   - copy installer\appsettings.installer.json → build-artifacts\api\appsettings.json
;     (replace __JWT_KEY__ with a random 64-char string)
;   - dotnet ef migrations bundle --self-contained -r win-x64 -o build-artifacts\efbundle.exe
;   - on Windows with Node 22.22.3, run scripts\package-whatsapp-qr.ps1
;     (puts the disabled-by-default test gateway inside build-artifacts\api)
;   - put SqlLocalDB.msi next to this file
;   - ISCC.exe SchoolServer-Setup.iss
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
#ifdef PreviewOnly
AppName=Queen International School TEST PREVIEW
#else
AppName=Queen International School
#endif
AppVersion={#AppVersion}
AppPublisher=Queen International School
DefaultDirName={autopf}\SmartSchool Server
DefaultGroupName=Queen International School
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist\installer
#ifdef PreviewOnly
OutputBaseFilename=QueenInternationalSchool-TEST-PREVIEW-{#AppVersion}
#else
OutputBaseFilename=QueenInternationalSchool-Setup-{#AppVersion}
#endif
SetupIconFile=Brand\QueenInternationalSchool.ico
UninstallDisplayIcon={localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
; API (Kestrel self-host — khud migrations+seed lagata hai startup par)
; appsettings.json alag line se — sirf tab daalo jab file PEHLE se na ho (upgrade par school ki config
; kabhi overwrite NAHI hoti: GitHub/ThingWorx/token waghaira settings mehfooz rehti hain)
Source: "..\build-artifacts\api\*"; DestDir: "{app}\api"; Flags: recursesubdirs ignoreversion; Excludes: "appsettings.json"
Source: "..\build-artifacts\api\appsettings.json"; DestDir: "{app}\api"; Flags: onlyifdoesntexist
; WPF Admin desktop (LocalAppData: self-updater kaam kare silently, koi UAC nahi)
Source: "..\build-artifacts\admin\SmartSchool.Admin.exe"; DestDir: "{localappdata}\Programs\SmartSchool Admin"; Flags: ignoreversion
; 1-click diagnostics and private Firebase server setup (no credentials bundled)
Source: "SmartSchool-Diagnostics.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "Configure-Firebase.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "Configure-Firebase.ps1"; DestDir: "{app}"; Flags: ignoreversion
; SQL Server LocalDB (bundled by CI — embedded, hamesha copy ho)
Source: "SqlLocalDB.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

#ifndef PreviewOnly
[InstallDelete]
; Purani (Program Files) wali admin exe saaf kar do taake shortcut sirf nayi ki taraf jaaye
Type: files; Name: "{app}\admin\SmartSchool.Admin.exe"
; CHANGED: remove the old API entry points so the new publish payload must replace them.
Type: files; Name: "{app}\api\SmartSchool.API.exe"
Type: files; Name: "{app}\api\SmartSchool.API.dll"
; CHANGED: old SmartSchool names (desktop + old Start Menu folder) hata kar Queen shortcut recreate karein.
Type: files; Name: "{group}\SmartSchool Admin.lnk"
Type: files; Name: "{group}\SmartSchool Pro.lnk"
Type: files; Name: "{commonprograms}\SmartSchool\SmartSchool Admin.lnk"
Type: files; Name: "{commonprograms}\SmartSchool\SmartSchool Pro.lnk"
Type: files; Name: "{userprograms}\SmartSchool\SmartSchool Admin.lnk"
Type: files; Name: "{userprograms}\SmartSchool\SmartSchool Pro.lnk"
Type: files; Name: "{commondesktop}\SmartSchool Admin.lnk"
Type: files; Name: "{commondesktop}\SmartSchool Pro.lnk"
Type: files; Name: "{commondesktop}\SmartSchool Diagnostics.lnk"
Type: files; Name: "{userdesktop}\SmartSchool Admin.lnk"
Type: files; Name: "{userdesktop}\SmartSchool Pro.lnk"
Type: files; Name: "{userdesktop}\SmartSchool Diagnostics.lnk"
#endif

[Icons]
Name: "{group}\Queen International School"; Filename: "{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe"; IconFilename: "{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe"
Name: "{group}\Queen School - Firebase Setup"; Filename: "{app}\Configure-Firebase.bat"; WorkingDir: "{app}"
Name: "{commondesktop}\Queen International School"; Filename: "{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe"; IconFilename: "{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe"
Name: "{commondesktop}\Queen School Diagnostics"; Filename: "{app}\SmartSchool-Diagnostics.bat"; IconFilename: "{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe"
; API auto-start at EVERY login (all users) — runs as the interactive user whose
; per-user LocalDB is proven to work. "--hide-console" = seedha background (koi window nahi).
#ifdef PreviewOnly
Name: "{commonstartup}\SmartSchool API (background)"; Filename: "{app}\api\SmartSchool.API.exe"; \
      Parameters: "--hide-console --urls http://127.0.0.1:5000"; WorkingDir: "{app}\api"; Flags: runminimized
#else
Name: "{commonstartup}\SmartSchool API (background)"; Filename: "{app}\api\SmartSchool.API.exe"; \
      Parameters: "--hide-console --urls http://0.0.0.0:5000"; WorkingDir: "{app}\api"; Flags: runminimized
#endif

[Run]
; 1. SQL Server 2022 LocalDB — silent + verbose MSI log (agar fail ho to diagnostics dikhayega)
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\SqlLocalDB.msi"" IACCEPTSQLLOCALDBLICENSETERMS=YES /qn /norestart /l*v ""C:\Windows\Temp\smartschool-localdb-msi.log"""; \
    StatusMsg: "SQL Server LocalDB install ho raha hai…(ek minute)"

; 2. Purani (broken) service ho to saaf kar do — nay model mein service NAHIN hai
#ifndef PreviewOnly
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated
#endif

; 3. Abhi ke login mein bhi API seedha shuru kar do (restart ka intezar nahi)
#ifdef PreviewOnly
Filename: "{app}\api\SmartSchool.API.exe"; Parameters: "--urls http://127.0.0.1:5000"; \
    WorkingDir: "{app}\api"; Flags: runhidden nowait postinstall; \
    StatusMsg: "SmartSchool test API shuru ho rahi hai…"
#else
Filename: "{app}\api\SmartSchool.API.exe"; Parameters: "--urls http://0.0.0.0:5000"; \
    WorkingDir: "{app}\api"; Flags: runhidden nowait postinstall; \
    StatusMsg: "SmartSchool API shuru ho rahi hai…"
#endif

[UninstallRun]
#ifndef PreviewOnly
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im SmartSchool.API.exe"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated
#endif

; CHANGED: stop a previously running API before replacing its binaries during an upgrade.
[Code]
#ifndef PreviewOnly
// Stops every running copy of the API (process, old service, anything still on port 5000) and
// waits until the port is free. Returns True only when the old API is really gone.
function StopOldApi(): Boolean;
var
  ResultCode: Integer;
  PsCmd: String;
begin
  ResultCode := 1;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop SmartSchoolApi', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM SmartSchool.API.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  PsCmd := '-NoProfile -ExecutionPolicy Bypass -Command "' +
    '$p = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue; ' +
    'if ($p) { $p | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue } }; ' +
    '$i = 0; while ((Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue) -and $i -lt 20) { Start-Sleep -Seconds 1; $i++ }; ' +
    'if (Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"';
  ResultCode := 1;
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if StopOldApi() then
    Result := ''
  else
    Result := 'Purani SmartSchool API abhi bhi chal rahi hai (port 5000). Task Manager mein SmartSchool.API.exe band karke setup dobara chalayein.';
end;
#else
// Preview is for a clean, separate PC. Refuse common signs of an existing
// school install/database. This is a safety net, not a substitute for a clean PC.
function InitializeSetup(): Boolean;
begin
  if DirExists(ExpandConstant('{autopf}\SmartSchool Server')) or
     FileExists(ExpandConstant('{localappdata}\Programs\SmartSchool Admin\SmartSchool.Admin.exe')) or
     FileExists(ExpandConstant('{localappdata}\Programs\SmartSchool Admin\appsettings.json')) or
     FileExists(ExpandConstant('{localappdata}\SmartSchoolAdmin\session.bin')) or
     FileExists(ExpandConstant('{commonstartup}\SmartSchool API (background).lnk')) or
     FileExists(ExpandConstant('{%USERPROFILE}\SmartSchoolPro.mdf')) then
  begin
    Log('PREVIEW_BLOCK_EXISTING_INSTALL');
    SuppressibleMsgBox('Existing school installation ya database mila. Yeh TEST PREVIEW live school PC par install na karein; alag clean test PC istemal karein.', mbError, MB_OK, IDOK);
    Result := False;
    exit;
  end;
  Result := SuppressibleMsgBox('Sirf clean, alag TEST PC par install karein. Live school server ya us ki database par yeh preview install NA karein. Test jaari rakhein?', mbConfirmation, MB_YESNO, IDNO) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  PsCmd: String;
begin
  if DirExists(ExpandConstant('{app}\api')) then
  begin
    Result := 'Is folder mein pehle se API hai. Test ke liye alag clean PC/folder chunein.';
    exit;
  end;
  // Never terminate a live process or steal its port in a preview installer.
  PsCmd := '-NoProfile -NonInteractive -Command "try { $p = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners(); if ($p | Where-Object { $_.Port -eq 5000 }) { exit 1 } else { exit 0 } } catch { exit 1 }"';
  ResultCode := 1;
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Result := 'Port 5000 pehle se istemal ho raha hai ya check nahi ho saka. Preview ne koi process band nahi kiya; alag clean test PC istemal karein.'
  else
    Result := '';
end;
#endif

[Messages]
#ifdef PreviewOnly
FinishedLabel=TEST PREVIEW sirf is test PC par install hua hai.%n%nAdmin app: Desktop / Start Menu «Queen International School»%nTest API: http://localhost:5000 — live server aur parent queue se alag.%nTest login: admin / Admin@12345.%n%nSirf alag test WhatsApp number aur ijazat wala test recipient istemal karein.
#else
FinishedLabel=Queen International School install ho gaya!%n%nAdmin app: Desktop / Start Menu shortcut «Queen International School»%nAPI: http://localhost:5000 — ABHI shuru ho gayi hai aur har PC-login par background mein khud uthe gi.%nLogin: admin / Admin@12345 — pehli dafa database apne aap ban jayegi.
#endif
