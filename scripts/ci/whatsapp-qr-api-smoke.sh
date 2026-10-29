#!/usr/bin/env bash
# Against an isolated CI database only. Never links WhatsApp or sends a message.
set -euo pipefail
base="${BASE_URL:-http://127.0.0.1:5000}"
fail() { echo "::error title=QR API smoke::$*"; exit 1; }
code() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }
[[ "$(code "$base/api/whatsapp-qr-test/qr")" == 401 ]] || fail 'Anonymous QR was not rejected.'
admin=$(curl -sS -X POST "$base/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin@12345"}' | jq -r '.data.token // empty')
[[ -n "$admin" ]] || fail 'Admin login failed.'
[[ "$(code "$base/api/whatsapp-qr-test/qr" -H "Authorization: Bearer $admin" -H 'Origin: https://untrusted.example')" == 403 ]] || fail 'Browser-origin QR access was not rejected.'
status=$(curl -sS "$base/api/whatsapp-qr-test/status" -H "Authorization: Bearer $admin")
[[ "$(printf '%s' "$status" | jq -r '.data.state')" == disabled ]] || fail 'QR gateway was not disabled by default.'
[[ "$(code -X POST "$base/api/whatsapp-qr-test/start" -H "Authorization: Bearer $admin" -H 'Content-Type: application/json' -d '{"riskConfirmed":false}')" == 400 ]] || fail 'Missing risk confirmation was accepted.'
[[ "$(code -X POST "$base/api/whatsapp-qr-test/send" -H "Authorization: Bearer $admin" -H 'Content-Type: application/json' -d '{"phone":"+923001234567","type":"pick","consentConfirmed":false,"requestId":"2f6b7620-9b59-4e2b-827a-d92994a2417d"}')" == 400 ]] || fail 'Missing recipient consent was accepted.'
username="qrtestparent$(date +%s)"
[[ "$(code -X POST "$base/api/auth/register" -H "Authorization: Bearer $admin" -H 'Content-Type: application/json' -d "{\"username\":\"$username\",\"password\":\"TestOnly123\",\"fullName\":\"Test Parent\",\"role\":\"Parent\"}")" == 201 ]] || fail 'Test parent creation failed.'
parent=$(curl -sS -X POST "$base/api/auth/login" -H 'Content-Type: application/json' -d "{\"username\":\"$username\",\"password\":\"TestOnly123\"}" | jq -r '.data.token // empty')
[[ -n "$parent" ]] || fail 'Test parent login failed.'
[[ "$(code "$base/api/whatsapp-qr-test/qr" -H "Authorization: Bearer $parent")" == 403 ]] || fail 'Parent access to QR was not rejected.'
[[ "$(code -X POST "$base/api/whatsapp-qr-test/start" -H "Authorization: Bearer $parent" -H 'Content-Type: application/json' -d '{"riskConfirmed":true}')" == 403 ]] || fail 'Parent access to start was not rejected.'
echo 'QR API authentication, default-off and consent checks passed without contacting WhatsApp.'
