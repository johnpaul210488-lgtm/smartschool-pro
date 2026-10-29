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

### Android APK and Firebase project

1. In Firebase Console create/select the project and register the parent Android
   app using the exact `applicationId` in the generated `android/app/build.gradle[.kts]`.
   The release pipeline's Flutter create command generates **`com.smartschool.smartschool_parent`**, not
   the previously documented `com.smartschool.parent`. Do not rename an already
   installed app to match the old docs.
2. Download that app's `google-services.json` (Android app config, not the
   service-account key). For a local build put it in `apps/parent_app/android/app/`
   and run `python3 scripts/configure_parent_android.py` from the repository root
   after `flutter create`. For the release workflow set GitHub Actions secret
   **`PARENT_GOOGLE_SERVICES_JSON`** to the entire JSON file. The release will
   stop if it is missing, malformed or does not match the generated package.
   Never commit the file or put a service-account private key in an APK/Actions secret.
3. iOS is separate: add the matching Apple app, put `GoogleService-Info.plist`
   in `ios/Runner/`, enable Push/Background modes and upload an **APNs key** in
   Firebase (Cloud Messaging). The Android script does not configure iOS.

### School API configuration

4. In the **same Firebase project**, generate a service-account private key and
   place it in a restricted folder on the school API host. Give the Windows user
   running the API read access. Enable Firebase and set the absolute path in the
   installed API's `appsettings.json` (normally
   `C:\Program Files\SmartSchool Server\api\appsettings.json`), then restart the API. The installer defaults to
   `Firebase:Enabled=false`; upgrading preserves the school's existing settings.
   A missing file, bad credentials, wrong Firebase project, disabled Cloud Messaging
   API, or offline server prevents delivery. Never share the private key or device tokens.
5. Install the new parent APK using the same signing key as the existing APK;
   generated Flutter projects default to debug signing, and ephemeral CI runners
   can produce different keys. Plan stable signing or, with the parent's consent,
   a reinstall (which clears app data). Log in, allow notification permission in
   Android settings, and verify Settings → App Notifications. “Device registered”
   only means the school API stored its token; it does not prove Firebase accepted
   or delivered a push.

### Parent-child link and diagnosis

6. Staff should verify the correct parent and student before linking: it grants
   the parent access to the child's information. Use an admin token in Swagger:
   `GET /api/parent-links/student/{studentId}` shows accounts and device counts
   (no tokens). If the correct active account is absent, use
   `POST /api/parent-links` with `{"parentUsername":"parent1","studentId":1}`;
   this is idempotent. Create a login via the staff-only `POST /api/auth/register`
   if necessary. After linking, restart the parent app to refresh SignalR groups.
   Never infer a link merely because two records share a phone number.
7. Check `C:\ProgramData\SmartSchool\logs\smartschool-*.log` (installer):
   - `FCM is disabled` / `[FCM DRY-RUN]`: **no push was sent**.
   - `FCM device registered for user ...`: token registration reached the API;
     the token itself is deliberately not logged.
   - `no active parent accounts are linked` / `no registered devices`: fix the link or
     open the configured app and sign in; use the staff-only link status endpoint.
   - `FCM target[...] failed` / initialization errors: investigate Firebase
     credentials, project and device token. `FCM accepted X/Y` means FCM accepted
     requests, not that the OS displayed them.
8. Test a **new** pick-in/drop-out while the parent app is in the background,
   then with it open. Background notifications require real FCM + permission;
   foreground may also show a SignalR banner. Avoid repeating the same event
   within the 30-second debounce window.

**WhatsApp is a separate channel:** fixing an FCM issue does not enable Twilio.
After deploying the constructor fix, existing `Queued` WhatsApp rows can be
re-enqueued by the sweeper and may send old events. Review `GET /api/whatsapp-logs?status=Queued`
and Twilio configuration before restarting; do not assume a `DRYRUN-*` entry was delivered.

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
