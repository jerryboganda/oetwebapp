#!/usr/bin/env bash
# Speaking module end-to-end smoke (curl-based).
# Signs in, creates a session, runs the warm-up→prep→active→end transitions,
# polls for AI assessment, prints the estimated readiness band.
set -euo pipefail

API="${OET_API_URL:-http://localhost:5199}"
EMAIL="${OET_TEST_LEARNER_EMAIL:-e2e-learner@example.com}"
PASSWORD="${OET_TEST_LEARNER_PASSWORD:-please-change-me}"

echo "[smoke] sign-in"
token=$(curl -fsS -H "Content-Type: application/json" \
  -X POST "$API/v1/auth/sign-in" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"rememberMe\":true}" \
  | jq -r '.accessToken')

auth=(-H "Authorization: Bearer $token")

echo "[smoke] pick a published role-play card"
card_id=$(curl -fsS "${auth[@]}" "$API/v1/speaking/role-play-cards" | jq -r '.[0].id')
if [[ -z "$card_id" || "$card_id" == "null" ]]; then
  echo "no published cards for this learner's profession — run seed-speaking-dev first." >&2
  exit 1
fi

echo "[smoke] create session for card=$card_id"
session_id=$(curl -fsS "${auth[@]}" -H "Content-Type: application/json" \
  -X POST "$API/v1/speaking/sessions" \
  -d "{\"rolePlayCardId\":\"$card_id\",\"mode\":\"AiSelfPractice\"}" \
  | jq -r '.session.id')

echo "[smoke] warm-up start + finish"
curl -fsS "${auth[@]}" -X POST "$API/v1/speaking/sessions/$session_id/start-warmup" >/dev/null
curl -fsS "${auth[@]}" -X POST "$API/v1/speaking/sessions/$session_id/finish-warmup" >/dev/null

echo "[smoke] start role-play, end immediately"
curl -fsS "${auth[@]}" -X POST "$API/v1/speaking/sessions/$session_id/start-roleplay" >/dev/null
curl -fsS "${auth[@]}" -X POST "$API/v1/speaking/sessions/$session_id/end" >/dev/null

echo "[smoke] trigger AI assessment"
curl -fsS "${auth[@]}" -X POST "$API/v1/speaking/sessions/$session_id/ai-assess" >/dev/null

echo "[smoke] poll for assessment..."
for i in {1..30}; do
  band=$(curl -fsS "${auth[@]}" "$API/v1/speaking/sessions/$session_id/ai-assessment" | jq -r '.readinessBand // empty')
  if [[ -n "$band" ]]; then
    echo "[smoke] PASS — readiness band: $band"
    exit 0
  fi
  sleep 2
done

echo "[smoke] FAIL — assessment did not land within 60s" >&2
exit 1
