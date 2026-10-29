# Final Integration Test Checklist

Run these in order on the school PC before go-live. Every ✅ has an expected result.

Setup: API running (`dotnet run --urls http://0.0.0.0:5000`), WPF logged in as admin,
driver app logged in as `driver1` (Bus #7), parent app as `parent1`, all on the same network.

## A. Pick-In chain (the critical one)

| # | Step | Expected |
|---|------|----------|
| A1 | Driver app → trip start (To School) | Maps screen shows live GPS; top bar "ONLINE" |
| A2 | Tap **PICK-IN** on a student | Green confirmation; event appears in driver history |
| A3 | WPF admin | Toast + beep "PICK-IN …"; bus attendance grid gets the row; live map pin |
| A4 | API console/log | `[WhatsApp DRY-RUN]` block (or real WhatsApp if enabled) with the formatted template |
| A5 | `GET /api/whatsapp-logs` | Row **Sent** with provider id `DRYRUN-*` (or Twilio SID) |
| A6 | Parent app (Firebase APK configured, parent linked, permission allowed; server Firebase enabled) | In background: OS notification. In foreground: one in-app banner (SignalR/FCM deduplicated); dashboard chip flips to **On Bus**; live bus marker moves |
| A7 | Repeat same tap within 30 s | Response says *duplicate tap ignored*; NO new attendance row, NO second WhatsApp |

## B. Drop-Out chain

| # | Step | Expected |
|---|------|----------|
| B1 | After several pick-ins, tap **DROP-OUT** | Same chain; parent chip → **At School** (morning trip) |
| B2 | Afternoon trip (To School=ToHome) drop-out | Chip → **Dropped Home** |
| B3 | WhatsApp template | DROPPED OFF wording (EN) or Urdu if `Twilio:Language=ur` / parent's app language is Urdu |

## C. Offline → online sync (driver app)

| # | Step | Expected |
|---|------|----------|
| C1 | Phone → airplane mode, trip stays open | GPS points queue in sqflite; top bar shows **OFFLINE** chip with pending count |
| C2 | Tap pick-in on 2 students while offline | Outbox count increases; events queued |
| C3 | Airplane mode OFF | Within seconds outbox drains, chip clears; WPF/parent apps receive both alerts; each arrives once |

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
| G1 | `Twilio:Enabled=true` with a **wrong** AuthToken → pick-in | Log rows stay Queued; API console shows retry attempts |
| G2 | Fast-path verify: set `WhatsAppQueue:RetryMinutes=1` temporarily | After 3 failed minutes → row **Failed** + WPF red toast "**WhatsApp FAILED**" |
| G3 | Restore correct token | `POST /api/whatsapp-logs/{id}/retry` (or next sweeper pass) → Sent |

## H. Load test — 50 concurrent students

```powershell
# On the school PC (scripts/load-test-bus-events.ps1) — fires 50 pick-ins in parallel
.\scripts\load-test-bus-events.ps1 -BaseUrl http://localhost:5000 -BusId 1 -FirstStudentId 1 -Count 50
```

| # | Expected |
|---|----------|
| H1 | All 50 requests return success within seconds |
| H2 | `GET /api/api/bus-attendance/today` shows 50 events |
| H3 | `GET /api/whatsapp-logs?status=Sent` count = 50 (+ mothers if enabled) — zero failures |
| H4 | WPF stays responsive; SignalR reconnects automatically if any blip |

## I. Sign-off

| Item | Owner | Date |
|------|-------|------|
| Pick-in + drop-out chains verified live (real WhatsApp + push) | | |
| Offline sync verified in the bus (real route drive) | | |
| Receipt printing on the school printer | | |
| 50-student load test passed | | |
| Backups scheduled (DB + firebase-sa.json + Twilio creds in vault) | | |
