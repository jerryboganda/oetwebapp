#!/bin/bash
# Detached gate runner: build, test, tsc, lint -> /tmp/g_*.log
set +e
cd /opt/oetwebapp || exit 99

echo "=== BUILD ==="
dotnet build backend/OetLearner.sln --nologo -v q > /tmp/g_build.log 2>&1
echo "BUILD_EXIT=$?" >> /tmp/g_build.log

echo "=== TEST ==="
dotnet test backend/OetLearner.sln -c Debug --no-build -m:1 --nologo --logger "console;verbosity=normal" > /tmp/g_test.log 2>&1
echo "TEST_EXIT=$?" >> /tmp/g_test.log

echo "=== TSC ==="
npx tsc --noEmit > /tmp/g_tsc.log 2>&1
echo "TSC_EXIT=$?" >> /tmp/g_tsc.log

echo "=== LINT ==="
npm run lint > /tmp/g_lint.log 2>&1
echo "LINT_EXIT=$?" >> /tmp/g_lint.log

echo "=== DONE ==="
