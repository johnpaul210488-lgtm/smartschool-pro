# SmartSchool Parent App (`smartschool_parent`)

Flutter app for parents: children cards + today's status (Not Boarded / On Bus /
At School / Dropped Home), live bus map, fee history + PDF receipts, pick/drop
alerts + announcements, EN ⇄ UR language switch, WhatsApp opt-in/out.

## One-time platform setup (first run on the dev PC)

This repo keeps only Dart source + pubspec (CI analyzes it). Generate the
native folders once — they are machine-local and ignored by git conventions:

```bash
cd apps/parent_app
flutter create --platforms android,ios --org com.smartschool --project-name smartschool_parent .
flutter pub get
```

## Run against your PC's API

```bash
# Physical phone on the same Wi-Fi → use the PC's LAN IP (SmartSchool.API must run with --urls http://0.0.0.0:5000)
flutter run --dart-define=API_BASE=http://192.168.1.10:5000

# Android emulator
flutter run --dart-define=API_BASE=http://10.0.2.2:5000
```

## Google Maps (live bus map)

Same key as the driver app. Add to `android/app/src/main/AndroidManifest.xml`:

```xml
<meta-data android:name="com.google.android.geo.API_KEY"
           android:value="YOUR_GOOGLE_MAPS_KEY"/>
```

iOS: `GMSApiClient.provideAPIKey("YOUR_KEY")` in `AppDelegate.swift` (plus
`google_maps_flutter_ios` setup from its README).

## Firebase (push notifications)

1. Firebase Console → create project → add Android app `com.smartschool.parent`
   → download `google-services.json` → put in `android/app/`.
2. `android/build.gradle` → add `classpath 'com.google.gms:google-services:4.4.2'`;
   `android/app/build.gradle` → `apply plugin: 'com.google.gms.google-services'`.
3. iOS: add Apple app → download `GoogleService-Info.plist` into
   `ios/Runner/` → enable Push Notifications + Background Modes (remote notifications).
4. Project settings → Service accounts → **Generate new private key** → save JSON
   on the API host → set in API `appsettings.json`:
   `"Firebase": { "Enabled": true, "CredentialsJsonPath": "C:\\secrets\\firebase-sa.json" }`.
5. Android 13+: the app asks POST_NOTIFICATIONS permission at first launch.

Without these files the app never crashes — FcmService just disables itself and
live alerts still come over SignalR.

## Login

Parent accounts are created by the admin (WPF → Students → parent account) and
linked to the child via a `ParentStudents` row. Demo seed (if `SeedDemoData`):

```
username: parent1   password: Parent@123
```

## Build for stores

```bash
flutter build appbundle --dart-define=API_BASE=https://api.yourschool.edu
flutter build apk --release --dart-define=API_BASE=<same>
flutter build ipa   # on macOS, after Xcode signing setup
```
