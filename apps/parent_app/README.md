# parent_app (Flutter — Phase 4)

Parent-side mobile app for Android + iOS. Created in Phase 4 of the build.

The Phase 1 API already scopes data server-side for parents:

- Login as Parent (`parent1 / Parent@123` in dev)
- `GET /api/bus-attendance/student/{id}` — your own child's pick/drop feed (403 for anyone else's)
- Child profile + outstanding fees via `GET /api/students/{id}` and `GET /api/fees/student/{id}`
- WhatsApp alerts out-of-band (Twilio); **instant in-app alerts** via SignalR: parents auto-join
  `ParentGroup_{studentId}` per child and receive the same `SendPickInAlert` / `SendDropOutAlert`
  events as the office
- **FCM push notifications** wired in Phase 4 against the (already created) `DeviceTokens` table

| Need | Package |
|---|---|
| State / DI | `riverpod` |
| HTTP + JWT | `dio` + `flutter_secure_storage` |
| Push | `firebase_messaging` (FCM) |
| Real-time | `signalr_netcore` |
| Maps (child's bus, optional) | `google_maps_flutter` |
