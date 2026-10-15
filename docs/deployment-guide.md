# SmartSchool Pro — Deployment Guide

Complete go-live checklist. Order: **Database → API → WPF → Driver app → Parent app → Twilio/Firebase → Smoke tests.**

---

## 1. Database (SQL Server)

**Option A — Azure SQL:** create database `SmartSchoolPro`, allow your API host's IP in the firewall.
**Option B — local/VPS:** SQL Server 2019+ (Express is fine for one school).

```bash
# Apply schema (idempotent — safe to run repeatedly)
sqlcmd -S <server> -d SmartSchoolPro -U sa -P <password> -i database/sqlserver/000_ef_reference_idempotent.sql
# …or let the API migrate automatically on first start (it runs Database.Migrate()).
```

Production connection string goes into API config (below). **Never** commit a real password.

---

## 2. API (ASP.NET Core 8)

**Option A — Azure App Service (Windows or Linux, .NET 8):**

```bash
cd src/SmartSchool.API
dotnet publish -c Release -o publish
# Zip the publish folder → Azure Portal → App Service → Deployment Center → Zip Deploy
# or: az webapp deploy --resource-group <rg> --name <app> --src-path publish.zip --type zip
```

**Option B — any VPS / school PC (Windows Service):**

```powershell
dotnet publish -c Release -o C:\apps\SmartSchoolApi
sc.exe create SmartSchoolApi binPath= "C:\apps\SmartSchoolApi\SmartSchool.API.exe --urls http://0.0.0.0:5000"
sc.exe start SmartSchoolApi
```

**Production `appsettings.Production.json` (or App Service → Configuration):**

```json
{
  "ConnectionStrings": { "DefaultConnection": "Server=…;Database=SmartSchoolPro;User Id=…;Password=…;Encrypt=True" },
  "Jwt": { "Key": "<64+ random chars — generate once, keep secret>", "ExpiresMinutes": 720 },
  "Twilio": {
    "Enabled": true, "AccountSid": "AC…", "AuthToken": "…",
    "WhatsAppFrom": "whatsapp:+1XXXXXXXXXX", "Language": "ur", "NotifyMother": true
  },
  "WhatsAppQueue": { "MaxRetries": 3, "RetryMinutes": 5, "SweepIntervalSeconds": 60 },
  "BusAttendance": { "DebounceSeconds": 30 },
  "GoogleMaps": { "ApiKey": "…" },
  "Firebase": { "Enabled": true, "CredentialsJsonPath": "C:\\secrets\\firebase-sa.json" },
  "SeedDemoData": false
}
```

CORS: allow only your origins in production. HTTPS strongly recommended (Let's Encrypt via win-acme on VPS, free and automatic on App Service).

Health check: `GET /health`.

---

## 3. WPF Admin Desktop (installable .exe)

```powershell
# Self-contained single-file build (no .NET install needed on admin PCs)
cd src\SmartSchool.Admin
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**Inno Setup** (free) — save as `installer.iss` next to the publish folder:

```iss
[Setup]
AppName=SmartSchool Admin
AppVersion=1.0.0
DefaultDirName={pf}\SmartSchool Admin
DefaultGroupName=SmartSchool Admin
OutputBaseFilename=SmartSchoolAdmin-Setup-1.0.0
Compression=lzma2

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\SmartSchool Admin"; Filename: "{app}\SmartSchool.Admin.exe"
Name: "{commondesktop}\SmartSchool Admin"; Filename: "{app}\SmartSchool.Admin.exe"
```

Compile with Inno → `SmartSchoolAdmin-Setup-1.0.0.exe` — installs like any Windows app.
Set the API URL in `%APPDATA%\SmartSchoolAdmin\settings.json` after first run (or ship a
`settings.json` beside the exe).

---

## 4. Firebase Cloud Messaging (parent + later driver app)

1. Firebase Console → **Create project** (no Google Analytics needed).
2. Add **Android app** `com.smartschool.parent` → download `google-services.json` → `android/app/`.
3. Gradle wiring (see `apps/parent_app/README.md` → Firebase).
4. iOS: add Apple app → `GoogleService-Info.plist` → `ios/Runner/` + enable Push/Background modes + upload **APNs key** in Firebase console (Cloud Messaging tab).
5. **Server side:** Project settings → Service accounts → Generate new private key →
   copy JSON to the API host → `Firebase:Enabled=true` + `Firebase:CredentialsJsonPath`.
6. Test: run the app, watch API logs — `[FCM]` line shows the registered token; trigger a pick-in → push arrives.

---

## 5. Twilio WhatsApp go-live

1. Twilio Console → Messaging → WhatsApp: complete sender approval (school name/logo + 2 message templates matching `WhatsAppTemplates.cs` exactly — the EN/UR texts are in that file).
2. Set `AccountSid`, `AuthToken`, `WhatsAppFrom` (approved number), `Enabled=true`.
3. Verify one live send (parent phone must join the sandbox *or* sender must be approved).
4. Ops: failed messages auto-retry 3× at 5-min → admins get a red `WhatsApp FAILED` toast in WPF; check `GET /api/whatsapp-logs?status=Failed`.

---

## 6. Mobile apps — store builds

```bash
# Driver app
cd apps/driver_app
flutter build appbundle  -t lib/main.dart --dart-define=API_BASE=https://api.yourschool.edu
flutter build apk --release ...
# flutter build ipa   (macOS + Xcode + Apple Developer account)

# Parent app
cd apps/parent_app
flutter create --platforms android,ios --org com.smartschool --project-name smartschool_parent .   # once
flutter build appbundle --dart-define=API_BASE=https://api.yourschool.edu
```

- **AAB** → Play Console → create app → upload under *Release → Production*.
- **IPA** → Xcode Organizer → upload to App Store Connect.
- Sign keystores: `keytool -genkey -v -keystore smartschool.keystore -keyalg RSA -validity 10000`
  → referenced in `android/key.properties` (never commit the keystore).

---

## 7. Final smoke test (10 minutes)

Follow **`docs/integration-test-checklist.md`** end-to-end: driver pick-in →
WPF toast+map → WhatsApp (dry-run or live) → parent push → status changes to
"On Bus" → drop-out → "Dropped Home" → fees → announcement → 50-event load script.

---

## 8. Backup & ops

- Azure SQL: automatic backups are on; verify restore once.
- Local SQL: schedule `sqlcmd -Q "BACKUP DATABASE SmartSchoolPro TO DISK='D:\backups\ss-%DATE%.bak'"` nightly (Task Scheduler).
- API logs: `logs/smartschool-*.log` (14-day retention) — check `WhatsApp FAILED` / `FCM` lines daily the first week.
