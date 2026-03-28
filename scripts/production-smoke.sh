#!/usr/bin/env bash
# production-smoke.sh — Post-deploy smoke test against a live Northwoods instance.
#
# Usage:
#   ./scripts/production-smoke.sh                          # defaults to https://northwoods.muness.com
#   SMOKE_BASE_URL=https://staging.example.com ./scripts/production-smoke.sh
#
# Exit codes:
#   0  All checks passed
#   1  One or more checks failed
#
# Requirements: curl, jq

set -euo pipefail

BASE_URL="${SMOKE_BASE_URL:-https://northwoods.muness.com}"
WORKER_EMAIL="${SMOKE_WORKER_EMAIL:-worker@sunrise.example}"
WORKER_PASSWORD="${SMOKE_WORKER_PASSWORD:-password}"
REVIEWER_EMAIL="${SMOKE_REVIEWER_EMAIL:-reviewer@sunrise.example}"
REVIEWER_PASSWORD="${SMOKE_REVIEWER_PASSWORD:-password}"

PASS=0
FAIL=0

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

check() {
  local name="$1"
  shift
  if "$@"; then
    PASS=$((PASS + 1))
    printf "  PASS  %s\n" "$name"
  else
    FAIL=$((FAIL + 1))
    printf "  FAIL  %s\n" "$name" >&2
  fi
}

login() {
  local email="$1" password="$2"
  local payload
  payload=$(jq -n --arg e "$email" --arg p "$password" '{email: $e, password: $p}')
  curl -sS -f -X POST "${BASE_URL}/api/auth/login" \
    -H "Content-Type: application/json" \
    -d "$payload" \
    --max-time 30
}

# ---------------------------------------------------------------------------
# 1. Homepage loads
# ---------------------------------------------------------------------------

homepage_check() {
  local http_code
  http_code=$(curl -sS -o /dev/null -w '%{http_code}' "${BASE_URL}/" --max-time 15)
  [ "$http_code" = "200" ]
}
check "Homepage returns 200" homepage_check

# ---------------------------------------------------------------------------
# 2. Worker login succeeds and returns a JWT
# ---------------------------------------------------------------------------

is_jwt() { [[ "$1" =~ ^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$ ]]; }

WORKER_TOKEN=""
worker_login() {
  local body
  body=$(login "$WORKER_EMAIL" "$WORKER_PASSWORD")
  WORKER_TOKEN=$(echo "$body" | jq -r '.accessToken // empty')
  [ -n "$WORKER_TOKEN" ] && is_jwt "$WORKER_TOKEN"
}
check "Worker login returns accessToken" worker_login

# ---------------------------------------------------------------------------
# 3. Reviewer login succeeds and returns a JWT
# ---------------------------------------------------------------------------

REVIEWER_TOKEN=""
reviewer_login() {
  local body
  body=$(login "$REVIEWER_EMAIL" "$REVIEWER_PASSWORD")
  REVIEWER_TOKEN=$(echo "$body" | jq -r '.accessToken // empty')
  [ -n "$REVIEWER_TOKEN" ] && is_jwt "$REVIEWER_TOKEN"
}
check "Reviewer login returns accessToken" reviewer_login

# ---------------------------------------------------------------------------
# 4. GET /api/templates returns 200 with worker token
# ---------------------------------------------------------------------------

check "GET /api/templates with worker token" \
  bash -c "[ -n '${WORKER_TOKEN}' ] && curl -sS -f '${BASE_URL}/api/templates' \
    -H 'Authorization: Bearer ${WORKER_TOKEN}' --max-time 15 | jq -e 'type == \"array\"' > /dev/null"

# ---------------------------------------------------------------------------
# 5. GET /api/review-queue returns 200 with reviewer token
# ---------------------------------------------------------------------------

check "GET /api/review-queue with reviewer token" \
  bash -c "[ -n '${REVIEWER_TOKEN}' ] && curl -sS -f '${BASE_URL}/api/review-queue' \
    -H 'Authorization: Bearer ${REVIEWER_TOKEN}' --max-time 15 | jq -e 'type == \"array\"' > /dev/null"

# ---------------------------------------------------------------------------
# 6. Bad credentials return an error, not a crash
# ---------------------------------------------------------------------------

bad_creds_check() {
  local http_code
  http_code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "${BASE_URL}/api/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"email":"bad@example.com","password":"wrong"}' \
    --max-time 15)
  # Expect a 4xx error (400 or 401), not a 5xx crash or 2xx success
  case "$http_code" in
    4[0-9][0-9]) return 0 ;;
    *) printf "    expected 4xx, got %s\n" "$http_code" >&2; return 1 ;;
  esac
}
check "Bad credentials return 4xx (not crash)" bad_creds_check

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printf "Production smoke: %d passed, %d failed\n" "$PASS" "$FAIL"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ "$FAIL" -gt 0 ]; then
  exit 1
fi
