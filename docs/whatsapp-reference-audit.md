# WhatsApp reference audit and SmartSchool design

**Reference audited:** VyaparApp 31.2.0 (Windows Electron build supplied as a three-part archive)  
**Audit date:** 30 October 2026  
**SmartSchool implementation:** provider-neutral durable outbox plus an isolated Baileys linked-device gateway

## Executive conclusion

The reference is not using the official WhatsApp Business Cloud API. It creates a multi-device WhatsApp Web client with WhiskeySockets/Baileys. Its useful reliability characteristics are encrypted local authentication state, QR/session restoration, explicit disconnect handling, persistent queues, serialized low-priority sending, bounded retries and a daily eligibility limit.

Those controls can reduce accidental bursts and data loss, but they cannot guarantee that a number will never be restricted. The reference product itself tells users to comply with WhatsApp terms and says that WhatsApp controls blocking/suspension. Its use of an unofficial client remains an account-policy risk.

SmartSchool therefore implements the useful behavior behind a provider boundary:

- `DryRun` for safe development;
- `Twilio` as the retained official managed provider;
- `LinkedDevice` as the opt-in QR provider requested for the school installer.

Linked-device mode is deliberately isolated in a loopback-only Node sidecar. It does not expose group, contact-scraping, broadcast or marketing endpoints. The WPF page requires an administrator to accept a clear compliance/unofficial-client warning before a QR can be generated.

## Audit method and scope

The supplied split archive was reconstructed and validated, the Electron installation was extracted, and `app.asar` was unpacked. Relevant core, task worker, service/UI and renderer bundles were expanded and deobfuscated. The review traced:

1. QR bootstrap and renderer state;
2. Baileys socket options and connection events;
3. credential/key storage and encryption;
4. queue persistence, dequeue order and timing;
5. send validation, daily limits and retries;
6. reconnect/logout/reset behavior;
7. the product-facing WhatsApp safety and unblocking guidance.

The implementation in this repository is a clean behavioral design. It does not copy reference source code.

## Reference behavior

### 1. Login and QR lifecycle

- The login screen initially shows a blurred placeholder QR.
- The real QR listener/bootstrap does not begin until the user checks the standard-usage terms acknowledgement.
- Baileys `connection.update` events supply each current QR.
- The socket sets a 60-second QR timeout. Replacement QR values are pushed back to the UI automatically.
- UI states include disconnected, connecting, previous session present, QR generated, connected and logged out.
- If encrypted local auth already contains registered credentials, startup restores that session instead of requiring another QR.

### 2. Session and local data persistence

- The client is created as a Baileys WhatsApp Connect client.
- Authentication credentials and signal/app-state keys are persisted locally and memory-cached.
- Contacts, messages, configuration and persistent queue state are local repositories.
- Stored values can be encrypted. An encryption key is protected by Electron/OS `safeStorage` and represented by a local `vp-e.key` key-vault file.
- An optional four-digit Smart Connect passcode is encrypted in `sc-passcode.key`; renderer IPC supports create, verify, existence check and reset.

### 3. Connection recovery

- Credential updates are persisted whenever Baileys emits them.
- On open, the connected number is saved and queue processing starts.
- `loggedOut`, `badSession` and `multideviceMismatch` invalidate/delete local auth and move to a logged-out state.
- Connection loss, timeout and ordinary closure reconnect when prior credentials exist.
- `restartRequired` performs a fresh bootstrap.
- Startup internet checks increase by three seconds per attempt, up to 72 seconds.
- Full logout flushes queues, terminates workers, clears caches/singletons, removes session/media state, resets counters and removes encryption material as applicable.

### 4. Message flow and queueing

- The persistent queue capacity is 10,000.
- High-priority work dequeues in batches of five.
- Low-priority work is serialized and waits for at least 4,500 ms between sends.
- Typing delays are 100/375/500 ms for high/medium/low priority.
- Retryable send failures are retried at most twice inside a 120-second retry window.
- Media uses bounded, separate work and temporary media is deleted on completion/failure.

### 5. Exact marketing eligibility rule

The reference’s marketing cap is not a simple cap on every message:

- It initializes the current-day count from locally recorded `fromMe=true` messages between local-day boundaries.
- A candidate consumes the configurable cap only when its priority is absent/below `HIGH` **and** the destination has no non-excluded inbound-message history.
- High-priority messages and destinations with prior inbound engagement bypass that particular marketing cap.
- The configured default is 300 per day.
- The in-memory count increments when a send is accepted for enqueue, before confirmed delivery.

This behavior must not be represented as proof of recipient consent. “A recipient once sent an inbound message” and “the recipient currently consents to this school alert” are different conditions.

### 6. Important claim-versus-code discrepancy

The reference guideline modal says messages are sent at “randomized intervals.” The inspected executable path did **not** reveal corresponding random-delay logic in the core or renderer. The concrete low-priority control found in executable logic is a fixed minimum interval of 4,500 ms.

SmartSchool does not repeat that reference claim as fact. Its own linked-device gateway implements actual cryptographically randomized spacing of 6–10 seconds by default.

### 7. Safety guidance in the reference

The product warns users to:

- comply with WhatsApp terms;
- avoid high-volume unsolicited messages;
- avoid rapid group joining and other suspicious group behavior;
- avoid unofficial clients;
- re-register or request an in-app/support review if a restriction is believed to be a mistake.

The “avoid unofficial clients” warning is notable because the audited integration itself uses Baileys. That contradiction is another reason not to promise “no bans.”

## SmartSchool implementation decisions

| Concern | SmartSchool behavior |
|---|---|
| Provider coupling | One `IWhatsAppSender` boundary selects DryRun, official Twilio or LinkedDevice. Attendance/controller code is provider-neutral. |
| QR flow | Admin-only API and WPF page; explicit terms/risk acceptance before QR bootstrap; live polling displays regenerated QR values. |
| Session restore | Encrypted Baileys auth state is detected and restored on gateway startup. |
| Secret storage | A random 256-bit AES-GCM session key and separate API bearer secret are generated locally. Windows protects the session key with current-user DPAPI; auth/receipt files are authenticated encrypted blobs written atomically. No QR, message body or full phone number is logged. |
| Network exposure | Gateway binds to `127.0.0.1` by default and refuses non-loopback binding unless explicitly overridden. API-to-gateway calls require a 256-bit bearer token; QR/consent APIs reject browser-origin and remote plain-HTTP access. |
| Process stability | API hosted supervisor starts/restarts the bundled portable gateway and stops only the process it owns. Installer gives the Node executable a product-specific process name. |
| Durable outbox | Database row is written before enqueue. Bounded 10,000-item wake-up lane, correlation dedupe and startup sweeper are preserved; SQL atomically claims a row before provider handoff. |
| Durable retries | `NextAttemptAtUtc` is persisted. Three total attempts fit inside a 120-second window; events older than five minutes and late retries are stopped instead of being presented as current alerts. |
| Idempotency | The database correlation GUID is sent to the gateway. Successful receipts are durably encrypted. A repeated key returns the original receipt. A crash/ambiguous outcome fails closed as `DeliveryUnknown` instead of risking an automatic duplicate. |
| Pacing | One strict send lane; no priority bypass; random 6–10 second interval. |
| Volume limits | 300 attempted linked-device sends per Pakistan day and 12 per recipient per rolling hour by default. Pending/unknown attempts consume limits. |
| Allowed use | Only one-to-one text with classification `transactional`; no group JIDs, broadcasts, bulk API, marketing API, scraping or contact import. |
| Consent | Staff must record explicit permission for the student’s current father/mother number. The worker revalidates that ledger immediately before handoff; parent `ReceiveWhatsApp` opt-out still overrides it. |
| Disconnect handling | Network closure/timeouts use jittered backoff capped at 72 seconds. Logout/bad session clears auth. Replaced/forbidden sessions do not reconnect aggressively. |
| Observability | Admin status shows provider, QR/session state, linked number, pacing and daily counters. Permanent failures retain a reason code and raise an admin alert without exposing the full number. |

## Reference behavior intentionally not copied

- The separate four-digit Smart Connect passcode is redundant inside SmartSchool’s authenticated, role-protected admin app.
- Inbound chats, contact scraping/sync and a local message-history product are outside the school-alert requirement and would increase private-data exposure.
- High-priority batches of five do not bypass SmartSchool’s linked-device safety lane.
- Prior inbound engagement does not bypass SmartSchool’s cap; the school must rely on current parent consent/opt-out state.
- The uncorroborated “randomized intervals” claim was replaced with a real, tested random interval.
- Temporary media queues were not added because this release exposes text-only transactional alerts.

## Why Twilio was not deleted

Twilio is an official, policy-safer production option and is not duplicate code once it sits behind the same provider interface. Removing it would force every deployment onto an unofficial protocol. What was removed is the Twilio-specific coupling:

- attendance templates/language/recipient settings now live under `WhatsApp`;
- dry-run is its own provider rather than `Twilio:Enabled=false` behavior;
- queue code no longer assumes Twilio;
- QR/session APIs exist only for the linked-device provider;
- legacy `Twilio:Enabled` and `WhatsAppQueue` settings are read only as an upgrade compatibility path.

## Operational policy

1. Use a dedicated school-owned number, not a staff member’s personal number.
2. Obtain and retain parent consent; honor opt-out immediately.
3. Use linked-device mode only for expected transactional alerts.
4. Keep the phone and WhatsApp app updated and online.
5. Do not raise pacing/cap defaults merely to clear a backlog.
6. Treat `blocked`, `forbidden` and `DeliveryUnknown` as human-review states.
7. If restrictions occur, stop automation and use WhatsApp’s official in-app review/support path.
8. Prefer Twilio/WhatsApp Business Platform where formal template approval, support and policy compliance are required.

## Validation targets

- Node unit tests cover encryption/tamper detection, atomic snapshots, Pakistan-day accounting, idempotent receipts, fail-closed ambiguous delivery, phone validation, bounded queueing and send spacing.
- Gateway integration smoke test verifies loopback health, bearer rejection and authenticated status.
- .NET CI builds all projects and generates the EF migration script.
- Installer CI stages production-only gateway dependencies and a portable Node runtime.
- Manual go-live check: generate QR, scan from Linked Devices, restart API/PC, confirm automatic restore, trigger one test bus event, verify a single receipt, unlink, and confirm a new QR is required.
