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

### Android APK and Firebase project

1. In Firebase Console create/select the project and register the parent Android
   app using the exact `applicationId` in the generated `android/app/build.gradle[.kts]`.
   The release pipeline's Flutter create command generates **`com.smartschool.smartschool_parent`**, not
   the previously documented `com.smartschool.parent`. Do not rename an already
   installed app to match the old docs.
2. The existing `apps/parent_app/firebase/google-services.json` is the **public
   Android client config** for project `queen-school`, not a service-account
   private key. The release workflow uses it by default (or the optional
   `PARENT_GOOGLE_SERVICES_JSON` Actions secret as an override), and validates
   both project and application ID. For a local build copy the client config to
   `apps/parent_app/android/app/` after `flutter create`, then run from the repo root:
   `python3 scripts/configure_parent_android.py --expected-project queen-school`.
   The API key in client config is exposed in every APK; restrict it appropriately
   in Google Cloud. Never put a service-account private key in an APK or public repo.
3. iOS is separate: add the matching Apple app, put `GoogleService-Info.plist`
   in `ios/Runner/`, enable Push/Background modes and upload an **APNs key** in
   Firebase (Cloud Messaging). The Android script does not configure iOS.

### School API configuration

4. On the school PC, after installing the updated server, run **Queen School -
   Firebase Setup** from the Start menu. Approve administrator access and select
   the service-account JSON for `queen-school` (not the Android client file).
   The helper validates the project, writes a copy to a restricted ProgramData
   folder and enables Firebase in `appsettings.json` without replacing the other
   settings; it stores a restricted backup. It grants read access to detected API
   and console users, administrators and LocalSystem. If the API runs as a different
   Windows account, give that account read access as well. No key is bundled in
   the installer. For custom installations set `Firebase__Enabled=true` and
   `Firebase__CredentialsJsonPath` yourself. Defaults are disabled and upgrading
   preserves the school's existing config. The helper does **not** restart the API:
   review queued WhatsApp messages first because they may be sent automatically on
   restart. Invalid credentials, wrong project, a disabled Cloud Messaging API or
   an offline server also prevent delivery. Never share private keys or device tokens.
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
Upgrade safety: migration `20261030120000_DurableWhatsAppSafety` retires unfinished
legacy `Queued`/QR rows instead of replaying old attendance alerts. Ambiguous old handoffs become
`DeliveryUnknown`; other unfinished rows become `legacy_not_replayed`. The retired v1.1.93 preview
session format is removed, so scan one new QR in **WhatsApp Connection** after this upgrade. A
`DRYRUN-*` entry from an older deployment was never delivered. Review failed/held history, but
create a fresh bus event for a new alert rather than replaying historical rows.

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
5. In the same page, search the test student and record permission only after the current father/mother recipient explicitly agrees.
6. Trigger one fresh bus event and verify exactly one message/receipt; revoke permission and confirm the next alert is held.
7. Never expose port 5110, copy the state directory, share QR screenshots, or use this endpoint for bulk/marketing/group automation.

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
