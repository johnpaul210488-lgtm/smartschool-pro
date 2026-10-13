# driver_app (Flutter — Phase 3)

Driver-side mobile app for Android + iOS. Created in Phase 3 of the build.

## Feature set (fully supported by the Phase 1 API)

- Login (`POST /api/auth/login`, Role = Driver)
- Start / end trip (`POST /api/trips/start`, `POST /api/trips/{id}/end`) — own bus auto-resolved
- Student manifest for the bus (`GET /api/buses/{id}/manifest`) with the day's status per child
- **PICK-IN / DROP-OUT** tap per student → `POST /api/bus-attendance/pick-in` / `.../drop-out`
  (studentId, tripId, device timestamp, GPS lat/lng)
- **Offline-first**: taps queued in a local SQLite outbox, replayed in order when the network
  returns (the API trusts the event's device timestamp, so parents' messages stay accurate)
- Background GPS streaming to the hub (`UpdateBusLocation`) during an active trip

## Response/contract notes

- Every API response is `{ success, message, data, errors }` - parse `data` only when `success`.
- Hub `/hubs/notifications?access_token={jwt}` is used for uplink GPS (driver role);
  pick-in/drop-out always go through REST first.

## Planned packages

| Need | Package |
|---|---|
| State / DI | `riverpod` |
| HTTP + JWT | `dio` + `flutter_secure_storage` |
| GPS (background) | `geolocator` + `flutter_background_service` (Android), background modes (iOS) |
| Offline outbox | `sqflite` |
| Maps | `google_maps_flutter` (same Google Maps key as the API) |
| Permissions | `permission_handler` |
| SignalR (GPS uplink) | `signalr_netcore` |

Base URL via `--dart-define=API_URL=http://<office-pc-ip>:5000`.
