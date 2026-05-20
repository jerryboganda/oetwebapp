#!/usr/bin/env bash
docker exec oet-api-green sh -c '
  echo "--- /var/opt/oet-learner/storage/ ---"
  ls -la /var/opt/oet-learner/storage/ 2>&1 | head -40
  echo "--- env Storage_/Upload_ ---"
  env | grep -iE "storage|upload|tmpdir|tmp_dir" | sort
  echo "--- find any *.bin staged ---"
  find /var/opt/oet-learner/storage -name "*.bin" 2>/dev/null | head -10
'
