# SmartSchool Driver — Flutter app (Android + iOS)

Driver ka mobile app: **START TRIP → bachon ko PICK-IN / DROP-OUT → background GPS → offline queue.**

## Pehli dafa setup (sirf 1 baar)

Yeh repo `lib/` + `pubspec.yaml` rakhta hai; platform ke folders (android/ios) aap apni PC par
generate karogay — yeh Flutter ka standard flow hai:

```powershell
cd apps\driver_app
flutter create . --project-name smartschool_driver --platforms android,ios
flutter pub get
```

> `flutter create .` hamari files ko touch nahi karta — sirf missing platform
> scaffolding banata hai. Hamara `android/app/src/main/AndroidManifest.xml`
> (permissions wali) use ho jayegi.

Phir:

```powershell
flutter run          # connected phone/emulator par
# ya APK banana:
flutter build apk --release      # build\app\outputs\flutter-apk\app-release.apk
```

## Server ka address

`lib/config/app_config.dart` mein:

| Kaam | `apiBaseUrl` |
|---|---|
| Android emulator + API same PC | `http://10.0.2.2:5000` |
| Asli phone + PC same Wi-Fi | `http://192.168.1.XX:5000` (PC ka LAN IP; API ko `dotnet run --urls http://0.0.0.0:5000` se chalao) |

Demo login: **driver1 / Driver@123** (Bus #7) • **driver2 / Driver@123** (Bus #12)

## Flow

1. **Login** → JWT `shared_preferences` mein save (app band/restart safe)
2. **Home** → `GET /api/buses/mine` → bus + route card; **START TRIP**
3. `POST /api/trips/start` → trip id; background service start (`flutter_background_service`)
4. **Trip screen** → `GET /api/buses/{id}/manifest` — har student ka card
5. **PICK-IN / DROP-OUT** tap → GPS fix + device timestamp → `POST /api/bus-attendance/pick-in|drop-out`
   - ✅ success → voice bole "Ali picked up" (flutter_tts, toggleable)
   - 📥 fail/offline → **SQLite outbox** mein queue; net wapas aye to auto-sync (attempts counter,
     duplicate-conflict ko gracefully drop karta hai)
6. Background service har **15 sec** `POST /api/trips/{id}/location` (REST uplink) —
   foreground notification: *"SmartSchool: Trip in progress — tracking active"*
7. **END TRIP** → GPS service stop → `POST /api/trips/{id}/end`

## Offline-first design decisions

- REST-first (user ka Phase-1 rule): pick/drop REST par; hub sirf admin-side listening ke liye
- GPS uplink bhi REST (background isolate mein SignalR socket reliable nahi hoti)
- Event ka **apna timestamp** bheja jata hai — API wahi trust karti hai, is liye delayed sync par
  bhi parents ko sahi time ka WhatsApp milta hai
- Optimistic UI: tap karte hi button "PICKED ✅" — driver ko wait nahi karna parta

## Structure

```
lib/
├── main.dart                      # Provider + routing shell
├── config/app_config.dart         # URL, intervals, notification
├── models/models.dart             # Envelope + DTO mirrors + PendingEvent
├── services/
│   ├── api_client.dart            # envelope-aware HTTP + JWT restore
│   ├── session_service.dart       # shared_preferences token store
│   ├── gps_service.dart           # geolocator wrapper (permissions)
│   ├── background_gps_service.dart# flutter_background_service (15s uplink)
│   ├── offline_outbox.dart        # sqflite FIFO queue
│   └── tts_service.dart           # voice confirmations
├── providers/app_state.dart       # sab state (ChangeNotifier)
└── screens/
    ├── login_screen.dart
    ├── home_screen.dart           # bus card + START/END TRIP
    ├── trip_screen.dart           # student cards: big PICK/DROP buttons
    └── widgets/status_banner.dart # offline bar + outbox + flash text
```

## iOS note

Xcode mein `ios/Runner/Info.plist` mein add karen:
`NSLocationWhenInUseUsageDescription`, `NSLocationAlwaysAndWhenInUseUsageDescription`,
aur Background Modes → "Location updates" + "Background fetch".
