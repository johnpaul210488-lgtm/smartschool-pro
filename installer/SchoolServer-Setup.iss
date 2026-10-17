; ============================================================================
; SmartSchool Pro — One-Click SCHOOL SERVER installer (Inno Setup)
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
;   - put SqlLocalDB.msi next to this file
;   - ISCC.exe SchoolServer-Setup.iss
; ============================================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppName=SmartSchool Pro (School Server)
AppVersion={#AppVersion}
AppPublisher=SmartSchool Pro
DefaultDirName={autopf}\SmartSchool Server
DefaultGroupName=SmartSchool
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist\installer
OutputBaseFilename=SmartSchoolServer-Setup-{#AppVersion}
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
; API (Kestrel self-host — khud migrations+seed lagata hai startup par)
Source: "..\build-artifacts\api\*"; DestDir: "{app}\api"; Flags: recursesubdirs ignoreversion
; WPF Admin desktop
Source: "..\build-artifacts\admin\SmartSchool.Admin.exe"; DestDir: "{app}\admin"; Flags: ignoreversion
; 1-click diagnostics
Source: "SmartSchool-Diagnostics.bat"; DestDir: "{app}"; Flags: ignoreversion
; SQL Server LocalDB (bundled by CI — embedded, hamesha copy ho)
Source: "SqlLocalDB.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\SmartSchool Admin"; Filename: "{app}\admin\SmartSchool.Admin.exe"
Name: "{commondesktop}\SmartSchool Admin"; Filename: "{app}\admin\SmartSchool.Admin.exe"
Name: "{commondesktop}\SmartSchool Diagnostics"; Filename: "{app}\SmartSchool-Diagnostics.bat"
; API auto-start at EVERY login (all users) — runs as the interactive user whose
; per-user LocalDB is proven to work. Console starts minimized in the background.
Name: "{commonstartup}\SmartSchool API (background)"; Filename: "{app}\api\SmartSchool.API.exe"; \
      Parameters: "--urls http://0.0.0.0:5000"; WorkingDir: "{app}\api"; Flags: runminimized

[Run]
; 1. SQL Server 2022 LocalDB — silent + verbose MSI log (agar fail ho to diagnostics dikhayega)
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\SqlLocalDB.msi"" IACCEPTSQLLOCALDBLICENSETERMS=YES /qn /norestart /l*v ""C:\Windows\Temp\smartschool-localdb-msi.log"""; \
    StatusMsg: "SQL Server LocalDB install ho raha hai…(ek minute)"

; 2. Purani (broken) service ho to saaf kar do — nay model mein service NAHIN hai
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated

; 3. Abhi ke login mein bhi API seedha shuru kar do (restart ka intezar nahi)
Filename: "{app}\api\SmartSchool.API.exe"; Parameters: "--urls http://0.0.0.0:5000"; \
    WorkingDir: "{app}\api"; Flags: runhidden nowait postinstall skipifsilent; \
    StatusMsg: "SmartSchool API shuru ho rahi hai…"

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im SmartSchool.API.exe"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated

[Messages]
FinishedLabel=SmartSchool Server install ho gaya!%n%nAdmin app: Desktop shortcut «SmartSchool Admin»%nAPI: http://localhost:5000 — ABHI shuru ho gayi hai aur har PC-login par background mein khud uthe gi.%nLogin: admin / Admin@12345 — pehli dafa database apne aap ban jayegi.
