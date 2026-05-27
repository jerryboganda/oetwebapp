#!/usr/bin/env bash
# Writing Module V2 smoke test — runs after Docker rebuild + boot.
# Hits the new V2 endpoints anonymously to confirm routing + content seeds.
set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
PASS=0
FAIL=0

check() {
  local label="$1"
  local cmd="$2"
  local expect="${3:-200}"
  local code
  code=$(eval "$cmd" 2>&1 | tail -1)
  if [ "$code" = "$expect" ]; then
    echo "PASS [$code] $label"
    PASS=$((PASS+1))
  else
    echo "FAIL [$code, expected $expect] $label"
    FAIL=$((FAIL+1))
  fi
}

echo "=== Writing V2 smoke ($BASE_URL) ==="

check "health/ready" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/health/ready" 200

# Anon GETs that should require auth → 401
check "writing/v2/profile (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/v2/profile" 401
check "writing/v2/pathway (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/v2/pathway" 401
check "writing/v2/today (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/v2/today" 401

# Anon GETs that may be public-listing (content browse) → 200 or 401 depending on auth gating
check "writing/v2/canon (browse rules)" "curl -s -o /dev/null -w '%{http_code}' '$BASE_URL/v1/writing/v2/canon'" 401
check "writing/scenarios?profession=medicine" "curl -s -o /dev/null -w '%{http_code}' '$BASE_URL/v1/writing/scenarios?profession=medicine&letterType=LT-RR'" 401
check "writing/exemplars?profession=medicine" "curl -s -o /dev/null -w '%{http_code}' '$BASE_URL/v1/writing/exemplars?profession=medicine&letterType=LT-RR'" 401
check "writing/mocks?profession=medicine" "curl -s -o /dev/null -w '%{http_code}' '$BASE_URL/v1/writing/mocks?profession=medicine'" 401
check "writing/showcase" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/showcase" 401
check "writing/mistakes" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/mistakes" 401

# Diagnostic POST (no body) → 401 anon
check "writing/diagnostic/start (no auth)" "curl -s -o /dev/null -w '%{http_code}' -X POST $BASE_URL/v1/writing/diagnostic/start" 401

# Admin → 401/403
check "admin/writing/scenarios (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/admin/writing/scenarios" 401
check "admin/writing/canon (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/admin/writing/canon" 401

# Tutor → 401/403
check "tutors/writing/queue (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/tutors/writing/queue" 401

# Tools → 401
check "tools/paraphrase (no auth)" "curl -s -o /dev/null -w '%{http_code}' -X POST $BASE_URL/v1/writing/tools/paraphrase" 401

# OCR → 401
check "ocr/upload (no auth)" "curl -s -o /dev/null -w '%{http_code}' -X POST $BASE_URL/v1/writing/ocr/upload" 401

# Stats → 401
check "stats/dashboard (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/v1/writing/stats/dashboard" 401

# Coach hub WS → 401/upgrade required
check "ws/writing/coach (no auth)" "curl -s -o /dev/null -w '%{http_code}' $BASE_URL/ws/writing/coach/test-session" 401

echo "=== Result: $PASS passed, $FAIL failed ==="
exit $FAIL
