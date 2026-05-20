#!/bin/bash
cd /opt/oetwebapp
nohup node scripts/admin/generate-listening.mjs --count 120 --resume > /tmp/generate-listening-live.log 2>&1 &
echo "PID=$!"
sleep 2
ps -ef | grep generate-listening | grep -v grep
