#!/usr/bin/env bash
# End-to-end smoke test against a running `docker compose up` stack.
# Web form (anti-forgery) → SQL Server (stored procedure + outbox) → worker → mock ERP → back.
# Usage: ./scripts/smoke.sh [web-url]
set -euo pipefail

WEB="${1:-http://localhost:5100}"
JAR="$(mktemp)"
trap 'rm -f "$JAR"' EXIT

wait_for() { # url, what
  for _ in $(seq 1 90); do
    if curl -fsS -o /dev/null "$1"; then return 0; fi
    sleep 2
  done
  echo "✗ $2 did not come up ($1)"; exit 1
}

echo "→ waiting for the web app"
wait_for "$WEB/health" "web"

echo "→ waiting for the ERP part catalogue to be synced by the worker"
for _ in $(seq 1 60); do
  count=$(curl -fsS "$WEB/api/malzemeler" | grep -o '"partNo"' | wc -l || true)
  [ "$count" -ge 16 ] && break
  sleep 2
done
[ "$count" -ge 16 ] || { echo "✗ materials not synced (found $count)"; exit 1; }
echo "  ✓ $count materials from the mock ERP"

material_id=$(curl -fsS "$WEB/api/malzemeler" | grep -o '"id":[0-9]*,"partNo":"PR-HP160-9"' | grep -o '[0-9]*' | head -1)
token=$(curl -fsS -c "$JAR" "$WEB/malzeme-talepleri/yeni" | sed -n 's/.*name="__RequestVerificationToken" type="hidden" value="\([^"]*\)".*/\1/p' | head -1)
need_date=$(date -d '+10 days' +%F 2>/dev/null || date -v+10d +%F)

echo "→ creating a material request through the MVC form (submitNow=true)"
status=$(curl -sS -b "$JAR" -o /dev/null -w '%{http_code}' -X POST "$WEB/malzeme-talepleri/yeni" \
  --data-urlencode "__RequestVerificationToken=$token" \
  -d "ProjectId=1" -d "NeedDate=$need_date" \
  -d "Lines[0].MaterialId=$material_id" --data-urlencode "Lines[0].Quantity=12,5" \
  -d "submitNow=true")
[ "$status" = "302" ] || { echo "✗ form post returned $status"; exit 1; }

echo "→ waiting for the worker to deliver it to the ERP"
# The API lists the newest message first: that is the one this script just created.
for _ in $(seq 1 30); do
  messages=$(curl -fsS "$WEB/api/entegrasyon/mesajlar")
  newest=${messages%%"},{"*}   # text up to the end of the first JSON object
  if echo "$newest" | grep -q '"status":"Processed"'; then
    key=$(echo "$newest" | grep -o '"idempotencyKey":"[^"]*"')
    erp=$(echo "$newest" | grep -o '"externalRef":"[^"]*"')
    echo "  ✓ $key processed → $erp"
    echo "✓ smoke test passed"
    exit 0
  fi
  sleep 2
done
echo "✗ message was not processed:"; echo "$messages"; exit 1
