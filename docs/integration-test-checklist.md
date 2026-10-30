# Final Integration Test Checklist

Run these in order on the school PC before go-live. Every ✅ has an expected result.

Setup: API running (`dotnet run --urls http://0.0.0.0:5000`), WPF logged in as admin,
driver app logged in as `driver1` (Bus #7), parent app as `parent1`, all on the same network.

## A. Pick-In chain (the critical one)

| # | Step | Expected |
|---|------|----------|
| A1 | Driver app → trip start (To School), then minimize and lock screen for 60 s | Persistent Trip notification remains; Admin/Parent live pin receives roughly one fresh point/second until END TRIP |
| A2 | Tap **PICK-IN** on a student | Green confirmation; event appears in driver history |
| A3 | WPF admin | Toast + beep "PICK-IN …"; bus attendance grid gets the row; live map pin |
| A4 | API console/log | `[WhatsApp DRY-RUN]` block (or real WhatsApp if enabled) with the formatted template |
| A5 | Admin → WhatsApp Connection → send activity (or `GET /api/whatsapp-logs`) | Exact student/event/father number row becomes **Sent** with provider ID; without permission it is **Held** and explains why |
| A6 | Parent app (Firebase APK configured, parent linked, permission allowed; Admin Firebase status ready) | In background/closed normally: OS notification. In foreground: one in-app banner (SignalR/FCM deduplicated); dashboard chip flips to **On Bus**; live bus marker moves. Explicit Android Force stop is not a valid background test |
| A7 | Repeat same tap within 30 s | Response says *duplicate tap ignored*; NO new attendance row, NO second WhatsApp |

## B. Drop-Out chain

| # | Step | Expected |
|---|------|----------|
| B1 | After several pick-ins, tap **DROP-OUT** | Same chain; parent chip → **At School** (morning trip) |
| B2 | Afternoon trip (To School=ToHome) drop-out | Chip → **Dropped Home** |
| B3 | WhatsApp template | DROPPED OFF wording (EN) or Urdu if `WhatsApp:Language=ur` / parent's app language is Urdu |

## C. Offline → online sync (driver app)

| # | Step | Expected |
|---|------|----------|
| C1 | Phone → airplane mode, trip stays open | Foreground tracker remains active and reports network errors; transient GPS pings are not backfilled |
| C2 | Tap pick-in on 2 students while offline | Attendance outbox count increases; events are stored with their original timestamps |
| C3 | Airplane mode OFF within five minutes | Within seconds outbox drains, chip clears; WPF/parent apps receive fresh alerts once |
| C4 | Keep an offline event for more than five minutes, then reconnect | Event appears in history, but stale WhatsApp/FCM alerts are not sent |

## D. Fees + receipts

| # | Step | Expected |
|---|------|----------|
| D1 | WPF → Fees: create fee for the demo child, mark **Pay** (receipt prints to default printer) | Receipt no `INV-…`; `PaidDate` set |
| D2 | Parent app → Fees tab | Paid row under history; pending rows in red/orange highlight; total due banner red when dues > 0 |
| D3 | Tap **Receipt / PDF** | Share sheet opens with rendered A5 PDF — save/share to WhatsApp |
| D4 | Try receipt endpoint for another parent's fee id (`GET /api/fees/my/999/receipt`) | 404/403 — data isolation holds |

## E. Parent settings → server behavior

| # | Step | Expected |
|---|------|----------|
| E1 | App → Settings → language ⇄ اردو | Whole UI + api `preferredLanguage` flips |
| E2 | New pick-in after switching to Urdu | WhatsApp log row shows the **Urdu** template for that parent |
| E3 | Settings → WhatsApp alerts OFF → new event | That parent gets **no** WhatsApp (log shows "skipped … disabled"); push still arrives |
| E4 | Change phone → save | `GET /api/parents/me/settings` returns the new number |

## F. Announcements

| # | Step | Expected |
|---|------|----------|
| F1 | `POST /api/announcements` (staff token) | Parent app Alerts→Announcements shows it; push arrives; WPF sees `ReceiveAnnouncement` |
| F2 | `DELETE /api/announcements/{id}` | Disappears from parent list (soft delete) |

## G. WhatsApp failure path

| # | Step | Expected |
|---|------|----------|
| G1 | LinkedDevice: stop gateway (or Twilio: use a wrong AuthToken) → pick-in | Durable row stays Queued while safe retries remain; `FailureCode` identifies the provider/session problem |
| G2 | Leave provider unavailable for the 120-second window | After at most 3 total attempts → row **Failed** + WPF admin alert; no late endless retry |
| G3 | Restore provider/session | `POST /api/whatsapp-logs/{id}/retry` → Sent, except `DeliveryUnknown` (blocked to prevent a duplicate) |

## H. Load test — 50 concurrent students

```powershell
# On the school PC (scripts/load-test-bus-events.ps1) — fires 50 pick-ins in parallel
.\scripts\load-test-bus-events.ps1 -BaseUrl http://localhost:5000 -BusId 1 -FirstStudentId 1 -Count 50
```

| # | Expected |
|---|----------|
| H1 | All 50 requests return success within seconds |
| H2 | `GET /api/api/bus-attendance/today` shows 50 events |
| H3 | With `DryRun`, `GET /api/whatsapp-logs?status=Sent` count = 50 (+ mothers if enabled). Do **not** burst-load a real linked number; its enforced 6–10 s lane must remain enabled. |
| H4 | WPF stays responsive; SignalR reconnects automatically if any blip |

## I. Sign-off

| Item | Owner | Date |
|------|-------|------|
| Pick-in + drop-out chains verified live (real WhatsApp + push) | | |
| Offline sync verified in the bus (real route drive) | | |
| Receipt printing on the school printer | | |
| 50-student load test passed | | |
| Backups scheduled (DB + Firebase/Twilio secrets in vault); linked-device state is not casually copied—relink on a replacement PC | | |
