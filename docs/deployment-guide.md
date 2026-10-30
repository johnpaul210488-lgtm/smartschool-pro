# SmartSchool Pro — Deployment Guide

Complete go-live checklist. Order: **Database → API/gateway → WPF → Driver app → Parent app → WhatsApp/Firebase → Smoke tests.**

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
  "WhatsApp": {
    "Provider": "LinkedDevice",
    "Language": "ur",
    "NotifyMother": true,
    "LinkedDevice": {
      "BaseUrl": "http://127.0.0.1:5110",
      "SecretsFile": "%LOCALAPPDATA%/SmartSchool/WhatsAppGateway/client-secret.json",
      "AutoStart": true,
      "NodeExecutable": "../whatsapp-gateway/SmartSchool.WhatsAppGateway.exe",
      "EntryPoint": "../whatsapp-gateway/src/index.js"
    },
    "Queue": {
      "Capacity": 10000, "MaxAttempts": 3, "RetryWindowSeconds": 120,
      "InitialRetryDelaySeconds": 15, "SweepIntervalSeconds": 5
    }
  },
  "Twilio": {
    "AccountSid": "AC…", "AuthToken": "…",
    "WhatsAppFrom": "whatsapp:+1XXXXXXXXXX"
  },
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

## 5. WhatsApp go-live

Read **`docs/whatsapp-reference-audit.md`** before selecting a provider.

`docker-compose.yml` is deliberately a SQL-only development helper. It does not expose the
API/gateway over a container network: development stays on `DryRun`, while the supported
linked-device production layout is the Windows installer with authenticated loopback traffic.

### Option A — linked device / QR (bundled school installer)

1. Confirm the gateway is healthy: `GET http://127.0.0.1:5110/health` on the school PC.
2. Sign in to WPF as Admin → **WhatsApp** → read/accept the compliance notice → **Connect / Generate QR**.
3. On the dedicated school phone: WhatsApp → Linked devices → Link a device → scan the current QR.
4. Restart the API/PC and confirm the encrypted session restores without another QR.
5. Trigger one consented parent test alert and verify exactly one message/receipt.
6. Never expose port 5110, copy the state directory, share QR screenshots, or use this endpoint for bulk/marketing/group automation.

Linked-device mode is unofficial and cannot guarantee against account restrictions. The gateway enforces randomized 6–10 second spacing, daily/per-recipient caps, a bounded lane and idempotent receipts. If WhatsApp warns, forbids or logs out the account, stop automation and use its in-app review/support process.

### Option B — official managed Twilio

1. Set `WhatsApp:Provider` to `Twilio`.
2. In Twilio Console → Messaging → WhatsApp, complete sender/template approval.
3. Set `Twilio:AccountSid`, `Twilio:AuthToken` and approved `Twilio:WhatsAppFrom`.
4. Verify one live send. Check failures with `GET /api/whatsapp-logs?status=Failed`.

For either provider, SmartSchool makes three total attempts inside 120 seconds. Permanent or ambiguous outcomes stop automatically and raise an admin alert.

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
