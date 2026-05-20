#!/usr/bin/env bash
# Probe backfill 500 cause.
set -e
echo '=== latest backfill-warning lines from sweep ==='
grep -nE 'backfill failed' /opt/oetwebapp/sweep-puredo.log | tail -3
echo
echo '=== full backfill 500 message (one example) ==='
grep -A1 'backfill failed' /opt/oetwebapp/sweep-puredo.log | head -5
echo
echo '=== api container logs containing backfill in last 5h ==='
docker logs learner-api-green --since 18000s 2>&1 | grep -i 'backfill' | head -20
echo
echo '=== exception types in api logs last 5h ==='
docker logs learner-api-green --since 18000s 2>&1 | grep -iE 'exception|error.*correlat' | head -10
