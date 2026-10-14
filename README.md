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
├── apps/driver_app                  # Flutter driver app        ⏳ Phase 3
├── apps/parent_app                  # Flutter parent app        ⏳ Phase 4
├── database/sqlserver/              # 001_schema.sql, 002_seed.sql, EF reference script
├── docs/                            # architecture.md, realtime-flow.md
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
| Fees | `GET/POST /fees` · `PUT/DELETE /fees/{id}` · `GET /fees/student/{id}` · `GET /fees/dues` · `POST /fees/{id}/pay` |
| Buses | `GET/POST /buses` · `PUT/DELETE /buses/{id}` · `PUT /buses/{id}/assign-driver` · `GET /buses/{id}/manifest` |
| Drivers | `GET/POST /drivers` · `PUT/DELETE /drivers/{id}` |
| **Bus attendance** | **`POST /bus-attendance/pick-in`** · **`POST /bus-attendance/drop-out`** · `GET /bus-attendance/student/{id}` · `GET /bus-attendance/bus/{id}` · **`GET /bus-attendance/today** |
| Trips | `POST /trips/start` · `POST /trips/{id}/end` · `GET /trips/active` · `GET /trips` |
| WhatsApp | `GET /whatsapp-logs` · `POST /whatsapp-logs/{id}/retry` |
| Dashboard | `GET /dashboard/summary` |

Authorization is role-based: `[Authorize(Roles="SuperAdmin,Admin")]` for mutations,
drivers can post bus events / manage their own trips, parents can only read their own children's data.

## SignalR — real-time contract

Hub: **`/hubs/notifications`** (JWT via `?access_token=`). Staff auto-join **AdminGroup**;
parents auto-join **ParentGroup_{studentId}** per child; staff can also `WatchBus(id)` → **BusGroup_{busId}**.

| Server → client event | Payload | Groups |
|---|---|---|
| `SendPickInAlert` | Bus event (student, bus, driver, PST time, GPS, location) | AdminGroup, ParentGroup_{studentId} |
| `SendDropOutAlert` | same | same |
| `UpdateBusLocation` | live bus GPS | AdminGroup, BusGroup_{busId} |
| `SendTripUpdate` | trip started/ended | AdminGroup, BusGroup_{busId} |

Client → server hub methods: `WatchBus`, `UnwatchBus`, `UpdateBusLocation` (driver GPS streaming).

## WhatsApp (Twilio) — config in `src/SmartSchool.API/appsettings.json`

```json
"Twilio": { "Enabled": false, "AccountSid": "", "AuthToken": "", "FromWhatsAppNumber": "whatsapp:+14155238886", "NotifyMother": false }
```

`Enabled:false` = **dry-run** (messages logged, marked Sent — the whole pipeline testable for free).
Messages are **persisted before sending** and retried with exponential backoff by a background queue
(5s → 30s → 2min), with a sweeper that recovers anything stuck after a restart.

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
