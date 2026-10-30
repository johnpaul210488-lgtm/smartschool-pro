# SmartSchool linked-device WhatsApp gateway

Isolated, loopback-only Baileys sidecar used when `WhatsApp:Provider` is `LinkedDevice`.

> This is an unofficial WhatsApp Web client. It cannot guarantee against account restrictions. Use only for consented transactional school alerts. For an official supported route, select the Twilio provider.

## Local development

Requires Node 20+ (release installer bundles Node 22).

```bash
npm ci
npm test
npm start
```

The gateway binds to `127.0.0.1:5110`. On first start it creates:

- `client-secret.json` — bearer key read by SmartSchool.API;
- `session.key` — local AES-256-GCM key, not read by the API;
- `auth-state.enc` — encrypted Baileys credentials/keys after linking;
- `send-receipts.enc` — encrypted idempotency/safety ledger after sending.

Windows defaults to `%LOCALAPPDATA%\SmartSchool\WhatsAppGateway`. Linux defaults to `$XDG_STATE_HOME/smartschool/whatsapp-gateway` or `~/.local/state/...`.

## Environment controls

| Variable | Default |
|---|---:|
| `SMARTSCHOOL_WHATSAPP_STATE_DIR` | platform local state path |
| `SMARTSCHOOL_WHATSAPP_HOST` | `127.0.0.1` |
| `SMARTSCHOOL_WHATSAPP_PORT` | `5110` |
| `SMARTSCHOOL_WHATSAPP_MIN_SEND_INTERVAL_MS` | `6000` (minimum allowed `4500`) |
| `SMARTSCHOOL_WHATSAPP_MAX_SEND_INTERVAL_MS` | `10000` |
| `SMARTSCHOOL_WHATSAPP_MAX_PER_DAY` | `300` |
| `SMARTSCHOOL_WHATSAPP_MAX_PER_RECIPIENT_HOUR` | `12` |
| `SMARTSCHOOL_WHATSAPP_MAX_PENDING` | `100` |
| `SMARTSCHOOL_WHATSAPP_QR_TIMEOUT_MS` | `60000` |
| `SMARTSCHOOL_WHATSAPP_LOG_LEVEL` | `info` |

Remote binding is rejected unless `SMARTSCHOOL_WHATSAPP_ALLOW_REMOTE=true`; do not enable it without a private authenticated reverse proxy and host firewall.

## Private API

All `/v1/*` requests require `Authorization: Bearer <apiKey>` from `client-secret.json`.

- `GET /health` — minimal unauthenticated supervisor health check;
- `GET /v1/session` — connection/QR/safety state;
- `POST /v1/session/connect` — `{ "acceptTermsAndRisk": true }`;
- `POST /v1/session/logout` — `{ "confirmUnlink": true }`;
- `POST /v1/messages` — one transactional text with a durable idempotency key.

There are intentionally no group, broadcast, inbound-message, contact or bulk-send endpoints.
