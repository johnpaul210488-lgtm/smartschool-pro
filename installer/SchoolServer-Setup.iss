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
; API (Kestrel self-host)
Source: "..\build-artifacts\api\*"; DestDir: "{app}\api"; Flags: recursesubdirs ignoreversion
; EF migration bundle (applies schema without any .NET SDK on the client PC)
Source: "..\build-artifacts\db\efbundle.exe"; DestDir: "{app}\db"; Flags: ignoreversion
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

; 2. Create + start + SHARE the instance (shared = accessible by the Windows
;    Service running as LocalSystem). Already-exists cases are no-ops.
Filename: "{commonpf64}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe"; Parameters: "create SmartSchool"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Database instance tayyar ho rahi hai…"
Filename: "{commonpf64}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe"; Parameters: "start SmartSchool"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Database instance shuru ho rahi hai…"
Filename: "{commonpf64}\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe"; Parameters: "share SmartSchool SmartSchoolShared"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Database share ho rahi hai…"

; 3. Apply ALL migrations → SmartSchoolPro database ready
Filename: "{app}\db\efbundle.exe"; Parameters: "--connection ""Server=(localdb)\\.\SmartSchoolShared;Database=SmartSchoolPro;Trusted_Connection=True;TrustServerCertificate=True"""; \
    StatusMsg: "SmartSchoolPro database ban rahi hai…"; Check: FileExists(ExpandConstant('{app}\db\efbundle.exe'))

; 4. API as an auto-start Windows Service (LocalSystem reaches the SHARED LocalDB)
;    Purani service ho to pehle hata do (upgrade pe fresh+correct binPath mile):
Filename: "{sys}\sc.exe"; Parameters: "stop SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete SmartSchoolApi"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "create SmartSchoolApi binPath= ""{app}\api\SmartSchool.API.exe"" --urls http://0.0.0.0:5000 start= auto"; \
    Flags: runhidden waituntilterminated; StatusMsg: "SmartSchool API service ban rahi hai…"
Filename: "{sys}\sc.exe"; Parameters: "start SmartSchoolApi"; \
    Flags: runhidden waituntilterminated; StatusMsg: "SmartSchool API service shuru ho rahi hai…"

[UninstallRun]
Filename: "cmd.exe"; Parameters: "/c sc.exe stop SmartSchoolApi & sc.exe delete SmartSchoolApi & exit /b 0"; Flags: runhidden waituntilterminated

[Messages]
FinishedLabel=SmartSchool Server install ho gaya!%n%nAdmin app: Desktop shortcut «SmartSchool Admin»%nAPI: http://localhost:5000 (service chal rahi hai)%nLogin: admin / Admin@123 — pehli dafa seed data ban jata hai.
