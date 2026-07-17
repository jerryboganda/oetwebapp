#!/usr/bin/env pwsh
# Seed the Speaking module locally for development. Idempotent.
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

Write-Host "[seed-speaking-dev] ensuring Postgres is reachable..."
$pg = Get-Process -Name "postgres" -ErrorAction SilentlyContinue
if (-not $pg) {
    Write-Warning "Postgres process not detected. Start it (docker compose up postgres) and retry."
    exit 1
}

Write-Host "[seed-speaking-dev] applying EF Core migrations..."
dotnet ef database update --project backend/src/OetWithDrHesham.Api --no-build

Write-Host "[seed-speaking-dev] running the API once to trigger seeders..."
$env:ASPNETCORE_ENVIRONMENT = "Development"
$logPath = Join-Path $env:TEMP "oet-seed-api.log"
$proc = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", "backend/src/OetWithDrHesham.Api", "--no-build", "--no-launch-profile", "--", "--urls", "http://localhost:5199") `
    -PassThru -RedirectStandardOutput $logPath

try {
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:5199/health" -TimeoutSec 2
            if ($r.StatusCode -eq 200) {
                Write-Host "[seed-speaking-dev] API ready — seeders done."
                break
            }
        } catch { Start-Sleep -Seconds 1 }
    }
} finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}

# TODO: when Agent H lands the explicit `--seed-speaking` CLI flag, swap the
# warm-start above for a one-shot seed CLI invocation.

Write-Host "[seed-speaking-dev] done."
