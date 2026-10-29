# SmartSchool Pro

[![SmartSchool API CI](https://github.com/johnpaul210488-lgtm/smartschool-pro/actions/workflows/api-ci.yml/badge.svg)](https://github.com/johnpaul210488-lgtm/smartschool-pro/actions/workflows/api-ci.yml)

Complete school management system: **WPF admin desktop app** (no browser, pure Windows .exe),
**Flutter driver & parent apps**, **ASP.NET Core 8 Web API**, **SignalR real-time alerts** and
**Twilio WhatsApp notifications** — all timestamps in **Pakistan Standard Time (UTC+5)**.

| Layer | Tech | Status |
|---|---|---|
| Backend API (layered) | ASP.NET Core 8, EF Core, SQL Server, JWT, SignalR, FluentValidation, Serilog | ✅ Phase 1 built |
| Database schema | T-SQL scripts + EF migrations | ✅ Phase 1 built |
| Admin Desktop App | C# .NET 8 WPF (MaterialDesign, MVVM, SignalR toasts+sound, live map) | ✅ DONE |
| Driver App | Flutter (Android/iOS), background GPS, offline queue | ⏳ Phase 3 |
| Parent App | Flutter (Android/iOS), FCM push + live child status | ⏳ Phase 4 |

---

## Solution structure

```
SmartSchool.sln
├── src/SmartSchool.API              # Web API: Program, Controllers, SchoolNotificationHub,
│   │                                #   Validators (FluentValidation), Middleware
│   ├── Controllers/                 # Auth, Students, Classes, Teachers, Fees, Buses, Drivers,
│   │                                # BusAttendance, Trips, WhatsAppLogs, Dashboard
│   ├── Hubs/SchoolNotificationHub.cs#   /hubs/notifications
│   ├── Middleware/                  # global exception -> uniform envelope
│   └── Validators/                  # FluentValidation rules
│
├── src/SmartSchool.Core             # Models (entities), DTOs, Interfaces (no dependencies)
├── src/SmartSchool.Infrastructure   # EF Core DbContext, Migrations, Repositories/UoW,
│                                    # Services: JWT, Twilio WhatsApp + durable queue, Google geocoding
├── src/SmartSchool.Shared           # Enums, Constants (roles/groups/events), Helpers
│                                    # (PkTime, PasswordHasher, PhoneNormalizer), ApiResponse envelope
├── src/SmartSchool.Admin            # ✅ Phase 2 — native WPF desktop: sidebar shell, dashboard cards,
│                                    #    students/fees/buses/drivers/reports, SignalR toasts+sound,
│                                    #    GMap.NET live tracking, FlowDocument receipt printing
├── apps/driver_app                  # ✅ Phase 3 — Flutter driver app: trip/student list, pick-in/drop-out,
│                                    #    15s background GPS, sqflite offline outbox + auto-sync, TTS
├── apps/parent_app                  # ✅ Phase 5 — Flutter parent app: children cards + today status,
│                                    #    live bus map, fees + PDF receipts, alerts/announcements, EN⇄UR
├── database/sqlserver/              # 001_schema.sql, 002_seed.sql, EF reference script
├── scripts/                         # load-test-bus-events.ps1 (50 concurrent events)
├── installer/                       # SchoolServer-Setup.iss (one-click: LocalDB+service+admin)
├── docs/                            # architecture.md, realtime-flow.md, deployment-guide.md,
│                                    #   integration-test-checklist.md, auto-update-and-installer.md,
│                                    #   fee-automation.md (fee plans, partial payments, upgrade path)
└── docker-compose.yml               # optional local SQL Server 2022
```

## Uniform response envelope (every endpoint)

```json
{ "success": true, "message": "Student created successfully", "data": { ... }, "errors": [] }
```

- Success → HTTP 200/201, `success: true`, payload in `data` (paged lists are
  `{ items, page, pageSize, totalCount, totalPages }`).
- Validation failure → HTTP 400, `success: false`, `message: "Validation failed."`, `errors: [ ... ]`.
- Not found / conflict / unauthorized → HTTP 404/409/401/403 with the same envelope.
- Unhandled exception → HTTP 500, same envelope (details only in Development), logged via Serilog.

---

## Quick start (Windows dev machine)

**Prereqs:** .NET 8 SDK, SQL Server 2022 (or `docker compose up -d`).

```powershell
cd SmartSchoolPro

# 1. Database - pick ONE:
#    A) EF migrations (recommended):
dotnet ef database update --project src/SmartSchool.Infrastructure --startup-project src/SmartSchool.API
#    B) or run database/sqlserver/001_schema.sql then 002_seed.sql in SSMS.

# 2. Adjust the connection string in src/SmartSchool.API/appsettings.json if needed
#    (LocalDB: "Server=(localdb)\\MSSQLLocalDB;Database=SmartSchoolPro;Trusted_Connection=True;")

# 3. Run the API
cd src/SmartSchool.API
dotnet run
# -> http://localhost:5000 redirects to Swagger
# phones on the school Wi-Fi:  dotnet run --urls http://0.0.0.0:5000
```

**4. Run the WPF Admin app (Phase 2)** — API chalti hi rahe, naye terminal mein:
```powershell
cd src/SmartSchool.Admin
dotnet run
# Login: admin / Admin@12345
# Single-file exe (kisi bhi Windows PC par bina .NET ke chalne wala):
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
# -> publish/SmartSchool.Admin.exe  (GitHub Actions 'SmartSchool Admin (WPF) CI' bhi yeh exe
#    build kar ke artifact ke tor par deta hai — Actions tab se download karo)
```

Swagger: `http://localhost:5000/swagger` • Health: `/health` • Hub: `/hubs/notifications`
Logs: console + `logs/smartschool-{date}.log` (Serilog, rolling daily, 14 days kept).

In **Development** (`SeedDemoData: true`) the app additionally seeds a teacher, a parent and
current-month fee challans for every sample student.

### Seeded data (per spec) & demo logins — change passwords on a real install

| What | Count / Value |
|---|---|
| Admin | 1 — `admin / Admin@12345` (SuperAdmin) |
| Classes | 2 — `1-A`, `2-A` |
| Students | 5 — Ahmed Raza, Fatima Noor, Usman Ghani (bus #7), Ayesha Siddiqui, Bilal Ahmed (bus #12) |
| Buses | 2 — `#7` (Route 1), `#12` (Route 2) |
| Drivers | 2 — `driver1 / Driver@123` (bus #7), `driver2 / Driver@123` (bus #12) |
| Dev extras | `teacher1 / Teacher@123`, `parent1 / Parent@123` (parent of Ahmed Raza), 5 fee challans |

---

## API endpoints (all prefixed `/api`)

| Area | Endpoints |
|---|---|
| Auth | `POST /auth/login` · `POST /auth/register` (Admin only) · `POST /auth/change-password` |
| Students | `GET/POST /students` · `GET/PUT/DELETE /students/{id}` (search, paging, class/bus filters) |
| Classes | `GET/POST /classes` · `PUT/DELETE /classes/{id}` |
| Teachers | `GET/POST /teachers` · `PUT/DELETE /teachers/{id}` |
| Fees | `GET/POST /fees` · `PUT/DELETE /fees/{id}` · `GET /fees/student/{id}` · `GET /fees/dues` · **`POST /fees/{id}/pay`** (full or **partial**) · **`GET /fees/{id}/payments`** (ledger) · **`GET /fees/legacy-partials`** (reconcile old rows) |
| **Fee plans** | **`GET /fee-plans/heads`** · **`GET/PUT /fee-plans/classes/{id}`** (draft template) · **`GET/PUT /fee-plans/students/{id}`** (overrides + opt-ins) · **`GET/PUT /fee-plans/assignments/class\|student/{id}`** (activation + due day + calendar) · **`POST /fee-plans/generate-now`** |
| Buses | `GET/POST /buses` · `PUT/DELETE /buses/{id}` · `PUT /buses/{id}/assign-driver` · `GET /buses/{id}/manifest` |
| Drivers | `GET/POST /drivers` · `PUT/DELETE /drivers/{id}` |
| **Bus attendance** | **`POST /bus-attendance/pick-in`** · **`POST /bus-attendance/drop-out`** · `GET /bus-attendance/student/{id}` · `GET /bus-attendance/bus/{id}` · **`GET /bus-attendance/today** |
| Trips | `POST /trips/start` · `POST /trips/{id}/end` · `GET /trips/active` · `GET /trips` |
| WhatsApp | `GET /whatsapp-logs` · `POST /whatsapp-logs/{id}/retry` |
| Dashboard | `GET /dashboard/summary` |
| **Parent app** | **`GET /parents/me/children`** (cards: photo/class/bus/today status) · `GET/PATCH /parents/me/settings` (language, WhatsApp opt-out, phone) · `GET /fees/my` · `GET /fees/my/{id}/receipt` |
| Devices (FCM) | `POST /devices/register` · `POST /devices/unregister` |
| Announcements | `GET /announcements` · `POST/DELETE /announcements` (staff → SignalR + FCM broadcast) |
| **App updates** | `GET /app-updates/{admin\|driver\|parent}` (version feed) · `GET /app-updates/{app}/download` (proxied asset from GitHub Releases — see **docs/auto-update-and-installer.md**) |

Authorization is role-based: `[Authorize(Roles="SuperAdmin,Admin")]` for mutations,
drivers can post bus events / manage their own trips, parents can only read their own children's data.

## Fee plans, automated billing & partial payments

Full detail: **docs/fee-automation.md**. Short version:

- **Nothing is charged** until a class (or single student) plan is *activated* with an
  effective date, due day and billing calendar. Saving a plan only stores a draft.
- Activation cannot be backdated, and **only the current period is billed** — old
  periods are never back-filled automatically. `POST /api/fee-plans/generate-now`
  issues the current period immediately; a background worker repeats the same
  idempotent sweep **every 30 minutes**.
- Bills are per **fee head** (`Tuition`, `Transport`, …) with a period key such as
  `Monthly:2026-10`; `UNIQUE (StudentId, FeeHeadId, BillingPeriodKey)` makes duplicates impossible.
- **Partial payments** are first-class: every payment is an immutable `FeePayments` row,
  `AmountPaid`/`Balance` are derived, status moves `Pending → Partial → Paid`,
  overpayment is rejected and a settled bill is read-only (audit trail intact).
- Optional heads (transport, computer, …) are billed only after a per-student opt-in.
- Upgrading an existing database keeps old receipts: legacy `Paid` rows get
  `AmountPaid = Amount` plus one ledger row; legacy `Partial` rows are **never guessed** —
  they are listed by `GET /api/fees/legacy-partials` and reconciled with
  `database/sqlserver/099_legacy_partial_reconciliation.sql`.

CI proves all of it on a real SQL Server 2022 (`.github/workflows/branch-verify.yml`):
plan activation → billing → idempotency → partial payment → settlement → dues, plus an
**upgrade-path** test that starts from the previous schema with legacy `Paid`/`Partial` rows.
Locally: `BASE_URL=http://localhost:5000 bash scripts/fees-api-smoke-test.sh`.

## SignalR — real-time contract

Hub: **`/hubs/notifications`** (JWT via `?access_token=`). Staff auto-join **AdminGroup**;
parents auto-join **ParentGroup_{studentId}** per child; staff can also `WatchBus(id)` → **BusGroup_{busId}**.

| Server → client event | Payload | Groups |
|---|---|---|
| `SendPickInAlert` | Bus event (student, bus, driver, PST time, GPS, location) | AdminGroup, ParentGroup_{studentId} |
| `SendDropOutAlert` | same | same |
| `UpdateBusLocation` | live bus GPS | AdminGroup, BusGroup_{busId} |
| `SendTripUpdate` | trip started/ended | AdminGroup, BusGroup_{busId} |
| `SendAdminAlert` | red flag (e.g. WhatsApp permanently failed) | AdminGroup |
| `ReceiveAnnouncement` | school announcement | AdminGroup, ParentsGroup |

Client → server hub methods: `WatchBus`, `UnwatchBus`, `UpdateBusLocation` (driver GPS streaming).

## WhatsApp (Twilio) — config in `src/SmartSchool.API/appsettings.json`

```json
"Twilio": { "Enabled": false, "AccountSid": "", "AuthToken": "", "WhatsAppFrom": "whatsapp:+14155238886", "Language": "en", "NotifyMother": false },
"BusAttendance": { "DebounceSeconds": 30 }
```

- `Enabled:false` = **dry-run** (messages logged, marked Sent — the whole pipeline testable for free).
- Official Twilio SDK (`Twilio.AspNet.Core`); legacy key `FromWhatsAppNumber` still accepted as fallback.
- **Templates** (`src/SmartSchool.Shared/Helpers/WhatsAppTemplates.cs`), per-school via `Twilio:Language`:
  - **en** — “Your child *{Name}* (Class {Class}) has BOARDED Bus #{Bus} / has been DROPPED OFF safely …”
  - **ur** (Roman-Urdu) — “Aap ka bacha … Bus #… mai baith chuka hai / se mehfooz utar chuka hai …”
- Messages are **persisted before sending** (`WhatsAppLogs`, dedupe key `CorrelationId`) and retried by a
  hosted background queue: **3 attempts at 5-minute intervals** (`WhatsAppQueue:RetryMinutes`),
  a sweeper that recovers anything stuck after a restart, then permanent **Failed** + a
  `SendAdminAlert` SignalR push so every connected admin desktop shows an error toast.
- **Rate limiting / debounce:** the same event type for the same student within
  `BusAttendance:DebounceSeconds` (default 30 s — covers double-taps and offline-sync re-posts)
  returns the original event: no duplicate attendance row, no second WhatsApp (idempotent).
- Per-parent overrides: the **parent app's language setting** picks that parent's template, and the
  **WhatsApp toggle** (`User.ReceiveWhatsApp`) skips that parent's number entirely.

## Push notifications (Firebase Cloud Messaging) — Phase 5

```json
"Firebase": { "Enabled": false, "CredentialsJsonPath": "" }
```

`Enabled:false` = **dry-run; no push is delivered**. Pick-in/drop-out sends FCM to registered
devices of explicitly linked parents; announcements target all parent devices. Token registration
alone does not prove delivery. The release APK requires the matching Android Firebase app config,
and the API needs service-account credentials from the same Firebase project. Staff can check and
create parent-child links through `/api/parent-links`. Setup and diagnostics:
**docs/deployment-guide.md → section 4**.

## Test the critical real-time flow in Swagger (2 minutes)

1. `POST /api/auth/login` → `driver1 / Driver@123` → **Authorize** with the token.
2. `POST /api/trips/start` → `{"busId": 1, "type": "ToSchool"}`.
3. `POST /api/bus-attendance/pick-in` → `{"studentId": 1, "latitude": 33.5869, "longitude": 71.4414}`.
4. Watch the dry-run WhatsApp message in the console/logs and `GET /api/whatsapp-logs`;
   the same payload went to `AdminGroup` + `ParentGroup_1` on SignalR.
5. `GET /api/bus-attendance/today` for the admin live feed.

## Security checklist before production

- Override `Jwt:Key` (≥ 32 chars) via user-secrets / env var `Jwt__Key`.
- Change seeded passwords; rotate Twilio credentials; serve HTTPS behind IIS/reverse proxy.
- Restrict the CORS policy if you ever expose a browser client.

## Conventions

- **DB = UTC, wire/UI = PST.** Timezone math lives only in `SmartSchool.Shared/Helpers/PkTime.cs`.
- Enums travel and are stored as strings (`"PickIn"`), with SQL CHECK constraints.
- Soft deletes for people/students; hard blocks where history exists (paid fees, buses with students).
- Errors are uniform (see envelope above); validation is 100% FluentValidation.
