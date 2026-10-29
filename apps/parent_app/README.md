# SmartSchool Parent App (`smartschool_parent`)

Flutter app for parents: children cards + today's status (Not Boarded / On Bus /
At School / Dropped Home), live bus map, fee history + PDF receipts, pick/drop
alerts + announcements, EN ⇄ UR language switch, WhatsApp opt-in/out.

## One-time platform setup (first run on the dev PC)

This repo keeps Dart source, pubspec and the existing public Firebase Android
client config. Generate the native folders once on the build machine; never
commit a Firebase service-account private key:

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

1. In Firebase Console, add the **actual existing Android application ID**. With
   the `flutter create` command above, it is `com.smartschool.smartschool_parent`
   (verify `android/app/build.gradle[.kts]` → `applicationId`). The previously
   documented `com.smartschool.parent` is **not** this release's ID. Do not
   change the application ID just to match the old documentation; that makes a
   different app which cannot update the existing installation.
2. The existing public Android client config for Firebase project `queen-school`
   is `firebase/google-services.json` (already used in the latest releases).
   For a local build, copy it to `android/app/google-services.json` and run
   `python3 scripts/configure_parent_android.py --expected-project queen-school`
   from the repository root. It validates the project and package, adds the
   Gradle plugin, Android notification permission and required desugaring.
3. GitHub Releases use the checked-in client config by default; the optional
   `PARENT_GOOGLE_SERVICES_JSON` Actions secret can override it with another
   **Android app config from the same project**. A missing, malformed or
   mismatched config fails the build. Client config is public in the APK; restrict
   its Google API key appropriately. Never put a service-account private key in
   this file, in the override, or in an APK.
4. On the school API host, run **Queen School - Firebase Setup** from the Start
   menu in the updated server installer and select the `queen-school` service-
   account private key. The helper protects the file and updates only Firebase
   settings. It does not restart the server: review queued WhatsApp messages first,
   because they may be sent automatically when the API restarts. The installer
   default is disabled; upgrading preserves the existing `appsettings.json`.
5. Install the new APK, sign in, allow notifications (Android 13+), and link the
   parent to the correct child. Updates must be signed with the same key as the
   installed APK. The generated Flutter project defaults to debug signing; an
   ephemeral CI runner's debug key may differ, so plan a stable signing key or a
   consented reinstall (which clears app data). In Settings → App Notifications,
   “device registered” confirms registration with the school API only, **not**
   actual Firebase delivery. Check server logs and test a real pick/drop in background.
6. For iOS, separately add the matching Apple app, place `GoogleService-Info.plist`
   in `ios/Runner/`, enable Push Notifications + Background Modes, and configure
   APNs in Firebase. The Android setup script does not configure iOS.

Without Firebase, a connected foreground app can still show a SignalR banner,
but it cannot receive background push notifications. See
[`docs/deployment-guide.md`](../../docs/deployment-guide.md#4-firebase-cloud-messaging-parent--later-driver-app)
for parent linking, diagnostics, and a live test.

## Login

A staff user can create a parent login via `POST /api/auth/register` and explicitly
link it to a child via `POST /api/parent-links`; verify both identities before
linking, because the parent receives access to that child's data. Check links
and registered device counts via `GET /api/parent-links/student/{studentId}`.
After a new link, restart the parent app to reconnect its live subscriptions.
These are staff-only endpoints; the current WPF student screen does not create
parent accounts/links. Demo seed (if `SeedDemoData`):

```
username: parent1   password: Parent@123
```

## Build for stores

```bash
flutter build appbundle --dart-define=API_BASE=https://api.yourschool.edu
flutter build apk --release --dart-define=API_BASE=<same>
flutter build ipa   # on macOS, after Xcode signing setup
```
