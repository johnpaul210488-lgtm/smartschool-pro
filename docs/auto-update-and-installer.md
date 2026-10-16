# Auto-Update + One-Click Installer System

**Maqsad:** aap code mein change karein → tag push karein → har client school ko update khud mil jaye — admin EXE bhi, driver/parent APKs bhi. Aur naye client school ko sirf **1 Setup.exe** chalani ho — SQL, database, service sab khud.

```
FLOW (developer side)
━━━━━━━━━━━━━━━━━━━━━━
  code change → commit → git tag v1.1.0 → git push --tags
        │
        ▼  GitHub Actions (release.yml)
  ┌────────────────────────────────────────────┐
  │ SmartSchool.Admin.exe   (version-stamped)  │
  │ driver-release.apk      (version-stamped)  │
  │ parent-release.apk      (version-stamped)  │
  │ SmartSchoolServer-Setup-1.1.0.exe          │
  └─────────────────┬──────────────────────────┘
                    ▼  GitHub Release (assets)
FLOW (client side — sab school server ke zariye, GitHub ki zaroorat nahi)
━━━━━━━━━━━━━━━━━━━━━━
  WPF start  →  GET /api/app-updates/admin   → naya? → toast + sidebar "⬆ Update"
                button → download → app band → nayi exe replace → khud restart ✅
  APK start  →  GET /api/app-updates/driver  → naya? → dialog "UPDATE NOW"
                → download → Android 1-tap install ✅ (server URL prefs mein mehfooz rehta hai)
```

## 0. Prerequisite: Update feed on karein (school server)

`src/SmartSchool.API/appsettings.json` → `Updates` block:

```json
"Updates": {
  "Repo": "OWNER/REPO",
  "GitHubToken": "",              // PRIVATE repo → read-only fine-grained PAT; public → ""
  "CacheMinutes": 30
}
```
Repo public ho to token ki zaroorat nahi. Private ho to `github.com/settings/personal-access-tokens` → fine-grained → sirf is repo ke liye **Contents: Read** → token appsettings mein.

## 1. Release kaise karein (aapka roz ka workflow)

```powershell
# changes commit karne ke baad
git tag v1.1.0
git push --tags
```
3–5 minute mein **GitHub → Releases** mein sab attach ho jayega. Bas — kuch aur nahi karna.

Version kahan se aati hai? **Sirf tag se.** CI khud exe/APK mein wohi version stamp karti hai (koi file manually bump karne ki zaroorat nahi).

## 2. Client ko update kaise milti hai

| App | Kya hota hai |
|---|---|
| **WPF Admin** | Startup par background check. Naya version → top-right toast + sidebar mein **"⬆ Update 1.1.0"** button. Click → % progress → app band → 2 second → nayi version khul jati hai. Purana config (`%APPDATA%\SmartSchoolAdmin`) + login mehfooz. |
| **Driver APK** | Home screen par check. Dialog "UPDATE AVAILABLE" → **UPDATE NOW** → download → Android ka install prompt (1 tap — Google ki security policy, silent install sirf Play Store ya MDM se hota hai). |
| **Parent APK** | Dashboard par same dialog. |

**Server link kabhi nahi tootta:** pehli dafa jo server address set kiya (login screen / Settings par), woh SharedPreferences mein hai — update APK usay nahi chheerti.

## 3. Naya client school: ONE-CLICK Setup.exe

Release se **`SmartSchoolServer-Setup-X.Y.Z.exe`** utha kar school ke PC par chalayein. Yeh chup-chap karta hai:

1. **SQL Server 2022 LocalDB** install (bundled MSI, silent)
2. `SmartSchool` instance create + **share** (taake Windows Service ko mil sake)
3. API exe copy + **`efbundle.exe` se complete database** create
4. **`SmartSchoolApi` Windows Service** create + start — PC band/on par API khud chale
5. WPF Admin install + Desktop shortcut

Install khatam → Desktop se **SmartSchool Admin** → login `admin/Admin@123` (demo seed ON hai installer mein; production mein `SeedDemoData` false kar dein `C:\Program Files\SmartSchool Server\api\appsettings.json` mein).

> ⚠️ **Firewall:** doosre PCs/phones se connect karne ke liye port 5000 allow karein (pehli run par prompt aayega).
> 🔑 APKs: release wali `driver-release.apk` / `parent-release.apk` phones par bhejein — login screen par server `http://<PC-IP>:5000` set karein, bas.

## 4. APK hand-build (release pipeline ke baghair)

```powershell
# signing keystore (EK dafa — isay ledger mein likh kar rakhna)
keytool -genkey -v -keystore D:\smartschool.keystore -alias smartschool -keyalg RSA -keysize 2048 -validity 10000

# driver — server address APK mein bake kar dein
cd apps\driver_app
flutter build apk --release --build-name=1.1.0 --dart-define=API_BASE=http://192.168.1.10:5000

# parent (pehli dafa platform folder banayein)
cd apps\parent_app
flutter create --platforms android --org com.smartschool --project-name smartschool_parent .
flutter build apk --release --build-name=1.1.0 --dart-define=API_BASE=http://192.168.1.10:5000
```
Output: `build\app\outputs\flutter-apk\app-release.apk`

> Hand-built APK par in-app updater tabhi kaam karega jab AndroidManifest mein `<uses-permission android:name="android.permission.REQUEST_INSTALL_PACKAGES"/>` ho (CI build mein khud add hoti hai) — `android/app/src/main/AndroidManifest.xml` mein `<manifest>` ke andar yeh line daal dein.

## 5. Manual Inno installer (CI ke baghair)

```powershell
# 1-2: API publish + installer config
dotnet publish src\SmartSchool.API -c Release -o build-artifacts\api
copy installer\appsettings.installer.json build-artifacts\api\appsettings.json   # __JWT_KEY__ replace karein

# 3: efbundle
dotnet tool install -g dotnet-ef
dotnet ef migrations bundle --self-contained -r win-x64 -p src\SmartSchool.Infrastructure -s src\SmartSchool.API -o build-artifacts\db\efbundle.exe

# 4: admin exe → build-artifacts\admin\SmartSchool.Admin.exe
# 5: SqlLocalDB.msi download → installer\ folder
# 6: Inno Setup 6 install → ISCC.exe installer\SchoolServer-Setup.iss
```

## 6. Troubleshooting

| Masla | Hal |
|---|---|
| Update check par kuch nahi hota | Server `Updates:Repo` set hai? GitHub par koi Release publish hai (tag se start)? `GET /api/app-updates/admin` browser token se test karein |
| WPF download fail | Server Private repo hai to `Updates:GitHubToken` chahiye; server logs mein `Asset download failed` dekhein |
| APK "Install blocked" | Phone → Settings → Install unknown apps → is app ko allow (1 dafa per lifecycle) |
| Installer par SQL step fail | Antivirus/Defender LocalDB MSI rok raha — temporarily off; ya SqlLocalDB.msi alag se install karke installer dobara chalayein |
| Service start nahi hui | `services.msc` → SmartSchoolApi → Start; logs: `C:\Program Files\SmartSchool Server\api\logs\` |
