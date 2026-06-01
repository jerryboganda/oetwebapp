#!/bin/bash
# Detached gate runner: build, test, tsc, lint -> /tmp/g_*.log
set +e
cd /opt/oetwebapp || exit 99

echo "=== BUILD ==="
dotnet build backend/OetLearner.sln --nologo -v q > /tmp/g_build.log 2>&1
echo "BUILD_EXIT=$?" >> /tmp/g_build.log

echo "=== TEST ==="
# Memory-bound host: split tests into batches so each xUnit collection runs in
# a fresh dotnet test process, letting the GC fully reclaim between batches.
# Conservative GC tuning prevents the WebApplicationFactory leak from snowballing
# into an OOM kill of the test host.
: > /tmp/g_test.log
TEST_TOTAL=0
TEST_FAIL=0
TEST_BATCH_FAIL=0
export DOTNET_GCServer=0
export DOTNET_GCConserveMemory=9
export DOTNET_TieredCompilation=1
run_batch() {
  local label="$1"
  local filter="$2"
  echo "--- BATCH: $label ---" >> /tmp/g_test.log
  dotnet test backend/OetLearner.sln -c Debug --no-build -m:1 --nologo \
    --filter "$filter" --logger "console;verbosity=normal" \
    >> /tmp/g_test.log 2>&1
  local rc=$?
  echo "BATCH_EXIT[$label]=$rc" >> /tmp/g_test.log
  if [ $rc -ne 0 ]; then TEST_BATCH_FAIL=$((TEST_BATCH_FAIL+1)); fi
}
run_batch "Listening"          "FullyQualifiedName~Listening"
run_batch "SpeakingConv"       "FullyQualifiedName~Speaking|FullyQualifiedName~Conversation"
run_batch "ReadingWriting"     "FullyQualifiedName~Reading|FullyQualifiedName~Writing"
run_batch "RecallIapSponsor"   "FullyQualifiedName~Recall|FullyQualifiedName~NativeIap|FullyQualifiedName~Sponsor"
run_batch "Rest"               "FullyQualifiedName!~Listening&FullyQualifiedName!~Speaking&FullyQualifiedName!~Conversation&FullyQualifiedName!~Reading&FullyQualifiedName!~Writing&FullyQualifiedName!~Recall&FullyQualifiedName!~NativeIap&FullyQualifiedName!~Sponsor"
TEST_TOTAL=$(grep -c '^  Passed ' /tmp/g_test.log)
TEST_FAIL=$(grep -c '^  Failed ' /tmp/g_test.log)
echo "TEST_PASSED=$TEST_TOTAL" >> /tmp/g_test.log
echo "TEST_FAILED=$TEST_FAIL" >> /tmp/g_test.log
echo "TEST_BATCH_FAIL=$TEST_BATCH_FAIL" >> /tmp/g_test.log
if [ $TEST_BATCH_FAIL -eq 0 ] && [ $TEST_FAIL -eq 0 ]; then
  echo "TEST_EXIT=0" >> /tmp/g_test.log
else
  echo "TEST_EXIT=1" >> /tmp/g_test.log
fi

echo "=== TSC ==="
pnpm exec tsc --noEmit > /tmp/g_tsc.log 2>&1
echo "TSC_EXIT=$?" >> /tmp/g_tsc.log

echo "=== LINT ==="
pnpm run lint > /tmp/g_lint.log 2>&1
echo "LINT_EXIT=$?" >> /tmp/g_lint.log

echo "=== DONE ==="
