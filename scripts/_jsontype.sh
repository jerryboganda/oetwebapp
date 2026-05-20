#!/bin/bash
PID="${1:-e223ca4919cd4e85af2c22570ad7be73}"
echo === Type ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "SELECT pg_typeof(\"ExtractedTextJson\") FROM \"ContentPapers\" WHERE \"Id\"='$PID';"
echo === First 1500 chars ===
docker exec oet-postgres psql -U oet_learner oet_learner -c "SELECT LEFT(\"ExtractedTextJson\"::text, 1500) FROM \"ContentPapers\" WHERE \"Id\"='$PID';"
