# Real-time flow — Pick-In / Drop-Out (the critical path)

```
(1) Driver app          (2) Driver app          (3) Driver app            (4) SmartSchool.API
 login → own bus   ────> POST /api/trips/start > tap PICK-IN/c───────>    POST /api/bus-attendance/pick-in
    JWT in header         Trip row: Active       on student (or           (or /drop-out)
                                                 DROP-OUT):               - FluentValidation
                                                 { studentId, busId?,     - resolve caller's driver profile
                                                   tripId?, timestampUtc?,  - 404 student / bad trip checks
                                                   latitude, longitude }  - reverse geocode (Google/GPS fallback)
                                                                          - INSERT BusAttendance
                                                                          - (5) SignalR:
                                                                               SendPickInAlert / SendDropOutAlert
                                                                               -> AdminGroup + ParentGroup_{studentId}
                                                                          - (6) INSERT WhatsAppLog (Queued,
                                                                               CorrelationId) -> enqueue WhatsAppJob
                                                                      <- 200 OK { success, message, data:{ eventId, ... } }
                                                                         (driver screen shows "Boarded ✓")

(5) WPF Admin Desktop (Phase 2)                                        (6/7) WhatsAppQueueHostedService
 HubConnection: /hubs/notifications?access_token=...                      channel -> Twilio (or dry-run)
 .withAutomaticReconnect()                                                success: Status=Sent, WhatsAppSent=true
 .on("SendPickInAlert", ...)  -> toast popup + SoundPlayer                failure: backoff 5s -> 30s -> 2min,
 .on("SendDropOutAlert", ...) -> toast + sound                            Failed after N; sweeper recovers
 .on("UpdateBusLocation", ...) -> live map                                stuck rows after restart
```

## Messages sent to the father (templates in `BusAttendanceController`)

- **Pick-In:** `Your child {Name} has boarded Bus #{X} at {time} from {location}. — SmartSchool Pro`
- **Drop-Out:** `Your child {Name} has been safely dropped at {location} at {time}. — SmartSchool Pro`

`"Twilio:NotifyMother": true` also sends to the mother. All times are Pakistan time.

## Offline behaviour

- The driver app (Phase 3) holds taps in a local SQLite outbox and replays them when the
  network returns; the API trusts the **device timestamp** (`TimestampUtc`), so the actual
  boarding moment is what's stored, messaged and reported — clamps only impossible future clocks.

## SignalR contract (what clients implement)

Hub: `/hubs/notifications` • JWT via `?access_token=` • auto group join on connect.

| Direction | Name | Payload / Args | Who |
|---|---|---|---|
| Server → client | `SendPickInAlert` | `BusEventNotification` | AdminGroup + ParentGroup_{studentId} |
| Server → client | `SendDropOutAlert` | `BusEventNotification` | AdminGroup + ParentGroup_{studentId} |
| Server → client | `UpdateBusLocation` | `BusLocationUpdate` | AdminGroup + BusGroup_{busId} |
| Server → client | `SendTripUpdate` | `TripNotification` | AdminGroup + BusGroup_{busId} |
| Client → server | `WatchBus(busId)` | int | staff (live map) |
| Client → server | `UnwatchBus(busId)` | int | staff |
| Client → server | `UpdateBusLocation(busId, lat, lng, speedKmh?)` | driver GPS streaming (Driver role) | relayed to AdminGroup + BusGroup |

> Pick-in/drop-out themselves intentionally go through REST (durability first), then the
> server broadcasts the SignalR events. Only transient GPS pings use the hub uplink.

## Try it from Swagger (2 minutes)

1. `POST /api/auth/login` → `driver1 / Driver@123` → **Authorize** with the returned token.
2. `POST /api/trips/start` → `{"busId":1,"type":"ToSchool"}`.
3. `POST /api/bus-attendance/pick-in` → `{"studentId":1,"latitude":33.5869,"longitude":71.4414}`.
4. `GET /api/whatsapp-logs` → composed message (dry-run Sent); `GET /api/bus-attendance/today` → the live feed.
5. `POST /api/trips/1/end` → completed trip with event count.
