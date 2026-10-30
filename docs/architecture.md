# SmartSchool Pro — Architecture

## Solution layout (layered)

```
SmartSchool.sln
├── SmartSchool.API            Web host: controllers, SchoolNotificationHub, validators,
│                              middleware, Program.cs (DI + Serilog + JWT + Swagger)
├── SmartSchool.Core           Entities, DTOs, Interfaces   (zero dependencies)
├── SmartSchool.Infrastructure EF Core DbContext + migrations, Repository<T>/UnitOfWork,
│                              JwtTokenService, WhatsApp providers + durable queue, Geocoding
├── SmartSchool.WhatsAppGateway Isolated loopback Baileys process: QR, encrypted auth, safe lane
└── SmartSchool.Shared         Enums, constants (AppRoles, HubGroups, HubEvents),
                               PkTime/PasswordHasher/PhoneNormalizer, ApiResponse envelope
```

Dependency rule: `API → Infrastructure → Core → Shared` (each only references the ones below it).

## Component map

```
                         +--------------------------------------------------+
                         |               Windows PC (School office)         |
                         |   SmartSchool.Admin.Desktop.exe  (WPF, .NET 8)   |
   JWT + REST (HTTPS)    |   - Students/Fees/Buses/Reports screens          |
  +--------------------->|   - SignalR client, AutomaticReconnect           |
  |                      |   - toast + sound on SendPickInAlert/DropOut     |
  |                      +--------------------------------------------------+
  |                                                     ^  wss: /hubs/notifications
  |                                                     |  groups: AdminGroup
  |   +-------------------------------------------------+---------------------------+
  |   |                 SmartSchool.API (ASP.NET Core 8)                             |
  |   |   Controllers -> IUnitOfWork -> SchoolDbContext                              |
  |   |   SchoolNotificationHub  (SendPickInAlert/SendDropOutAlert/UpdateBusLocation)|
  |   |   FluentValidation -> uniform {success,message,data,errors} envelope         |
  |   |   Serilog (console + rolling file)                                           |
  |   +------+---------------------------+-------------------------------------------+
  |          | EF Core (SQL Server)      | HTTPS                    | HTTPS
  |          v                           v                          v
  |   +--------------+     +---------------------------+      +---------------------+
  |   |  SQL Server  |     | WhatsApp provider boundary|      | Google Maps Geocode |
  |   +--------------+     | DryRun / Twilio / local QR|      +---------------------+
  |                        +---------------------------+
  |                                      |
  |                                      v
  |                                Parents' WhatsApp
  |
  |    +----------------------------+       +------------------------------+
  +--> |   Driver App (Flutter)     | JWT   |   Parent App (Flutter)       |
       |  login, trips, pick/drop   +------> |  child feed/fees, FCM push  |
       |  offline outbox, GPS       | REST  |  ParentGroup_{studentId}     |
       +----------------------------+       +------------------------------+
```

## Module → endpoint map

| # | Module | Endpoints (`/api`) |
|---|--------|--------------------|
| 1 | Student Management | `GET/POST/PUT/DELETE /students`, `GET /students/{id}` |
| 2 | Class & Section | `GET/POST/PUT/DELETE /classes` |
| 3 | Fees | `GET/POST/PUT/DELETE /fees`, `GET /fees/student/{id}`, `GET /fees/dues`, `POST /fees/{id}/pay` |
| 4 | Bus & Transport | `GET/POST/PUT/DELETE /buses`, `PUT /buses/{id}/assign-driver`, `GET /buses/{id}/manifest`; `GET/POST/PUT/DELETE /drivers` |
| 5 | Real-Time Tracking | `POST /bus-attendance/pick-in`, `POST /bus-attendance/drop-out`, `GET /bus-attendance/student/{id}`, `GET /bus-attendance/bus/{id}`, `GET /bus-attendance/today`, trips, hub |
| 6 | WhatsApp Engine | durable outbox + provider boundary; logs/retry plus admin-only `/whatsapp/session` QR lifecycle |
| 7 | Attendance (class) | ⏳ later phase (Teacher module) |
| 8 | Reports & Dashboard | `GET /dashboard/summary` (charts/exports with the WPF shell) |

## Cross-cutting design

- **Uniform envelope** `{ success, message, data, errors }` — produced by controllers,
  FluentValidation failures (via `InvalidModelStateResponseFactory`) and the exception middleware.
- **Repository + Unit of Work** in Infrastructure; controllers never see `DbContext`.
- **Auth**: JWT (HMAC-SHA256), full-URI claim types for `role`/`name`; roles enforced via
  `[Authorize(Roles = ...)]` with the `AppRoles` constants.
- **Validation**: 100% FluentValidation (`AddFluentValidationAutoValidation`).
- **Logging**: Serilog — console + `logs/smartschool-.log` rolling daily; every HTTP request logged.
- **Real-time**: `SchoolNotificationHub` at `/hubs/notifications`; groups **AdminGroup**,
  **ParentGroup_{studentId}**, **BusGroup_{busId}**; events `SendPickInAlert`, `SendDropOutAlert`,
  `UpdateBusLocation`, `SendTripUpdate`.
- **WhatsApp outbox**: DB row first (deduped by CorrelationId), bounded wake-up channel,
  durable retry schedule, three total attempts inside 120 seconds, restart sweeper and manual retry.
  The linked-device sidecar adds encrypted idempotent receipts, a strict randomized 6–10 second lane,
  daily/per-recipient limits and no group/bulk surface.
- **Time**: DB stores UTC; `PkTime` converts to PST (Asia/Karachi) at the boundary.

## Scale notes (500+ students, 20+ buses)

- Paged endpoints everywhere; indexes on every FK and hot filter column (verified in migration).
- SignalR fan-out targets groups, never broadcast.
- Linked-device sends are intentionally slow (one every 6–10 seconds); the database absorbs bursts and the sweeper wakes up to 100 due rows per pass.

## Remaining build order

1. **Phase 2 — WPF Admin Desktop App** (`src/SmartSchool.Admin.Desktop`).
2. **Phase 3 — Driver App** (`apps/driver_app`).
3. **Phase 4 — Parent App** (`apps/parent_app`) + FCM wiring to `DeviceTokens`.
