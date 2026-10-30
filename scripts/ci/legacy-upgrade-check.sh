#!/usr/bin/env bash
# Verifies the fee-automation migration on a database that already holds finance data:
#   * the upgrade must not fail because of legacy 'Partial' rows
#   * legacy Paid rows keep their money: AmountPaid = Amount + one ledger row
#   * legacy Partial rows stay untouched (no invented amounts) and are listed for reconciliation
#   * no duplicate receipts / lost invoices
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5000}"
TOKEN=""
LAST_BODY=""
pass=0

fail() {
  local msg="❌ $*"
  echo "$msg" >&2
  if [ -n "$LAST_BODY" ]; then echo "--- response ---" >&2; echo "$LAST_BODY" >&2; fi
  local detail
  detail="$(printf '%s' "$msg")"
  if [ -n "$LAST_BODY" ]; then
    detail="$detail%0A$(printf '%s' "$LAST_BODY" | tr '\n' ' ' | cut -c1-1200)"
  fi
  echo "::error title=Legacy upgrade check failed::$detail"
  exit 1
}
ok() { pass=$((pass + 1)); echo "✅ $*"; }

api() {
  local method="$1" path="$2"
  if [ -n "$TOKEN" ]; then
    curl -sS -X "$method" "$BASE_URL$path" -H "Accept: application/json" -H "Authorization: Bearer $TOKEN" -w $'\n%{http_code}'
  else
    curl -sS -X "$method" "$BASE_URL$path" -H "Accept: application/json" -w $'\n%{http_code}'
  fi
}
body_of() { printf '%s' "$1" | sed '$d'; }
code_of() { printf '%s' "$1" | tail -n1; }
num_eq() { awk -v a="$1" -v b="$2" 'BEGIN { exit (a+0 == b+0 ? 0 : 1) }' || fail "$3: expected $2, got '$1'"; }

for _ in $(seq 1 60); do
  curl -sS -o /dev/null "$BASE_URL/health" && break
  sleep 2
done

r="$(curl -sS -X POST "$BASE_URL/api/auth/login" -H "Content-Type: application/json" \
      -d '{"username":"admin","password":"Admin@12345"}' -w $'\n%{http_code}')"
[ "$(code_of "$r")" = "200" ] || { LAST_BODY="$(body_of "$r")"; fail "login failed"; }
TOKEN="$(body_of "$r" | jq -r '.data.token')"
ok "admin can log in after the upgrade"

r="$(api GET "/api/fees?pageSize=200")"
[ "$(code_of "$r")" = "200" ] || { LAST_BODY="$(body_of "$r")"; fail "fee list failed after upgrade"; }

PAID_ID="$(body_of "$r" | jq -r '[.data.items[] | select(.receiptNo=="INV-202609-000001")][0].id')"
PAID_AMOUNT_PAID="$(body_of "$r" | jq -r --argjson id "${PAID_ID:-0}" '[.data.items[] | select(.id==$id)][0].amountPaid')"
PAID_BALANCE="$(body_of "$r" | jq -r --argjson id "${PAID_ID:-0}" '[.data.items[] | select(.id==$id)][0].balance')"
[ "$PAID_ID" != "null" ] || fail "legacy paid fee is missing after the upgrade (fees found: $(body_of "$r" | jq -r '.data.totalCount'), receipts: $(body_of "$r" | jq -rc '[.data.items[].receiptNo]'))"
num_eq "$PAID_AMOUNT_PAID" 5000 "legacy paid fee amountPaid"
num_eq "$PAID_BALANCE" 0 "legacy paid fee balance"
ok "legacy paid fee keeps its money (paid 5000, balance 0)"

r="$(api GET "/api/fees/$PAID_ID/payments")"
[ "$(code_of "$r")" = "200" ] || { LAST_BODY="$(body_of "$r")"; fail "payment history failed for a legacy fee"; }
COUNT="$(body_of "$r" | jq -r '.data | length')"
num_eq "$COUNT" 1 "legacy paid fee ledger rows"
ok "legacy payment appears once in the ledger"

r="$(api GET "/api/fees/legacy-partials")"
[ "$(code_of "$r")" = "200" ] || { LAST_BODY="$(body_of "$r")"; fail "legacy partial endpoint failed"; }
COUNT="$(body_of "$r" | jq -r '.data | length')"
num_eq "$COUNT" 1 "legacy partial rows listed"
PAID_ON_PARTIAL="$(body_of "$r" | jq -r '.data[0].amountPaid')"
num_eq "$PAID_ON_PARTIAL" 0 "legacy partial amountPaid (must never be guessed)"
ok "legacy partial row is listed for reconciliation with no invented amount"

PARTIAL_ID="$(body_of "$r" | jq -r '.data[0].id')"
r="$(api GET "/api/fees/$PARTIAL_ID/payments")"
COUNT="$(body_of "$r" | jq -r '.data | length')"
num_eq "$COUNT" 0 "ledger rows for the legacy partial fee"
ok "no fake payment rows were created for the legacy partial fee"

r="$(api GET "/api/whatsapp-logs?pageSize=200")"
[ "$(code_of "$r")" = "200" ] || { LAST_BODY="$(body_of "$r")"; fail "WhatsApp upgrade audit list failed"; }
QUEUED_STATUS="$(body_of "$r" | jq -r '[.data.items[] | select(.message=="LEGACY-QUEUED-SENTINEL")][0] | (.status + ":" + .failureCode)')"
HANDOFF_STATUS="$(body_of "$r" | jq -r '[.data.items[] | select(.message=="LEGACY-HANDOFF-SENTINEL")][0] | (.status + ":" + .failureCode)')"
[ "$QUEUED_STATUS" = "Failed:legacy_not_replayed" ] || fail "legacy queued WhatsApp row was not safely retired: $QUEUED_STATUS"
[ "$HANDOFF_STATUS" = "Failed:DeliveryUnknown" ] || fail "legacy ambiguous WhatsApp handoff was not blocked: $HANDOFF_STATUS"
ok "legacy WhatsApp queue and ambiguous handoff were retired without replay"

echo
echo "All $pass upgrade checks passed."
