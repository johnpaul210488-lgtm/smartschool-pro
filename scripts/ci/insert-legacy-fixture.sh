#!/usr/bin/env bash
# Inserts the legacy finance fixture into the CI SQL Server container and prints row counts.
# Every failure path is surfaced as a GitHub annotation (CI logs are not always retrievable).
set -uo pipefail

DB="${DB:-SmartSchoolPro}"
SA_PASS="${SA_PASS:-Your_strong!Passw0rd}"
SQL_FILE="${1:-scripts/ci/legacy-fee-fixture.sql}"

ann() { # ann MESSAGE TITLE
  local flat
  flat="$(printf '%s' "$1" | tr -d '\r' | tr '\n' ' ' | cut -c1-1500)"
  printf '::error title=%s::%s\n' "$2" "$flat"
}

CID="$(docker ps --filter ancestor=mcr.microsoft.com/mssql/server:2022-latest --format '{{.Names}}' | head -n1)"
[ -n "$CID" ] || CID="$(docker ps --filter publish=1433 --format '{{.Names}}' | head -n1)"
[ -n "$CID" ] || { ann "No SQL Server service container found (docker ps)." "Legacy fixture"; exit 1; }
echo "container: $CID"

SQLCMD="/opt/mssql-tools18/bin/sqlcmd"
if ! docker exec "$CID" test -x "$SQLCMD"; then SQLCMD="/opt/mssql-tools/bin/sqlcmd"; fi
echo "sqlcmd: $SQLCMD"

if ! OUT="$(docker cp "$SQL_FILE" "$CID":/tmp/fixture.sql 2>&1)"; then
  ann "docker cp failed: $OUT" "Legacy fixture"
  exit 1
fi

if ! OUT="$(docker exec "$CID" "$SQLCMD" -b -S localhost -U sa -P "$SA_PASS" -C -d "$DB" -I -i /tmp/fixture.sql 2>&1)"; then
  echo "$OUT"
  ann "fixture SQL failed: $OUT" "Legacy fixture"
  exit 1
fi
echo "$OUT"

COUNTS_SQL="SET NOCOUNT ON; SELECT CONCAT('students=', (SELECT COUNT(*) FROM dbo.Students), ' classes=', (SELECT COUNT(*) FROM dbo.Classes), ' fees=', (SELECT COUNT(*) FROM dbo.Fees), ' paid=', (SELECT COUNT(*) FROM dbo.Fees WHERE [Status]=N'Paid'), ' partial=', (SELECT COUNT(*) FROM dbo.Fees WHERE [Status]=N'Partial')) AS Counts;"
if ! OUT="$(docker exec "$CID" "$SQLCMD" -b -S localhost -U sa -P "$SA_PASS" -C -d "$DB" -I -h -1 -W -Q "$COUNTS_SQL" 2>&1)"; then
  echo "$OUT"
  ann "row count query failed: $OUT" "Legacy fixture"
  exit 1
fi
echo "rows before upgrade: $(printf '%s' "$OUT" | tr -d '\r' | tr '\n' ' ')"
