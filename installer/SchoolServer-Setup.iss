; ============================================================================
; SmartSchool Pro — One-Click SCHOOL SERVER installer (Inno Setup)
;
; What it does, silently, in order:
;   1. Installs SQL Server 2022 LocalDB (bundled MSI)
;   2. Creates + shares the "SmartSchool" LocalDB instance (Windows-Service-safe)
;   3. Lays down the API (Kestrel exe) + applies ALL EF migrations (efbundle)
;   4. Creates & starts the "SmartSchoolApi" Windows Service (auto-start)
;   5. Installs the WPF Admin app + Desktop shortcut
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
; SQL Server LocalDB (bundled by CI)
Source: "SqlLocalDB.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: FileExists(ExpandConstant('{src}\SqlLocalDB.msi'))

[Icons]
Name: "{group}\SmartSchool Admin"; Filename: "{app}\admin\SmartSchool.Admin.exe"
Name: "{commondesktop}\SmartSchool Admin"; Filename: "{app}\admin\SmartSchool.Admin.exe"

[Run]
; 1. SQL Server 2022 LocalDB — silent
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\SqlLocalDB.msi"" IACCEPTSQLLOCALDBLICENSETERMS=YES /qn /norestart"; \
    StatusMsg: "SQL Server LocalDB install ho raha hai…(ek minute)"; \
    Check: FileExists(ExpandConstant('{src}\SqlLocalDB.msi'))

; 2. API as an auto-start Windows Service. The service runs as LocalSystem, and
;    uses (localdb)\MSSQLLocalDB — SYSTEM ka apni private LocalDB instance hai jo
;    API startup par khud auto-create/restart hoti hai (DbInitializer migrations +
;    seed bhi khud lagata hai). Koi manual SQL/share step ki zaroorat nahi.
;    Purani service ho to pehle hata do (upgrade pe fresh+correct binPath mile):
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "create SmartSchoolApi binPath= ""{app}\api\SmartSchool.API.exe"" --urls http://0.0.0.0:5000 start= auto"; \
    Flags: runhidden waituntilterminated; StatusMsg: "SmartSchool API service ban rahi hai…"
Filename: "{sys}\sc.exe"; Parameters: "description SmartSchoolApi ""SmartSchool Pro school-server API (http://0.0.0.0:5000)"""; \
    Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure SmartSchoolApi reset= 60 actions= restart/60000/restart/60000/restart/60000"; \
    Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start SmartSchoolApi"; \
    Flags: runhidden waituntilterminated; StatusMsg: "SmartSchool API service shuru ho rahi hai…"

[UninstallRun]
Filename: "cmd.exe"; Parameters: "/c sc.exe stop SmartSchoolApi & sc.exe delete SmartSchoolApi & exit /b 0"; Flags: runhidden waituntilterminated

[Messages]
FinishedLabel=SmartSchool Server install ho gaya!%n%nAdmin app: Desktop shortcut «SmartSchool Admin»%nAPI: http://localhost:5000 (service chal rahi hai)%nLogin: admin / Admin@123 — pehli dafa seed data ban jata hai.
