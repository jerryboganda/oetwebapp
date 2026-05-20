#!/usr/bin/env bash
cd /opt/oetwebapp/backend/src/OetLearner.Api/Data/Migrations
for f in 20260518120000_AddUploadScannerRuntimeSettings 20260518124500_AddPayPalRuntimeSettings 20260518131500_AddPushVapidRuntimeSettings; do
  echo "=== $f ==="
  grep -E 'AddColumn|CreateTable|AlterColumn|CreateIndex|RenameColumn|DropColumn|DropTable' "$f.cs" | head -25
done
