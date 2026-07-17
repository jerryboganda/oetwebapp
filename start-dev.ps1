# OET with Dr Hesham - Hybrid Dev Launcher (Windows)
# ─────────────────────────────────────────────────────────────────────────────
# Database + .NET API run in Podman (containerized).
# Next.js frontend runs NATIVELY on Windows for fast, efficient hot reload.
#
# Why hybrid: on this Windows host the Podman machine's disk I/O is slow
# (Next.js reports "Slow filesystem detected ~13s"), so a containerized dev
# server cannot hot-reload efficiently. Native `pnpm dev` gives ~1s hot reload.
# Measured: containerized /sign-in = 240s+ timeout; native = 31s cold, ~1s edit.
# ─────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  OET with Dr Hesham - Hybrid Dev (DB+API in Podman, Frontend native)" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# 1. Start containerized database + API
Write-Host "`n[1/3] Starting Podman: PostgreSQL + .NET API..." -ForegroundColor Yellow
podman compose -f docker-compose.hotreload.yml --env-file .env.docker-local up -d

# 2. Wait for the API to report healthy
Write-Host "`n[2/3] Waiting for API health (http://localhost:8080/health)..." -ForegroundColor Yellow
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:8080/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { Start-Sleep -Seconds 5 }
}
if ($ready) {
    Write-Host "      API is healthy." -ForegroundColor Green
} else {
    Write-Host "      API not healthy yet — it may still be compiling. Check: podman logs oet-hotreload-api" -ForegroundColor Red
}

# 3. Start the native Next.js dev server (foreground; Ctrl+C to stop)
Write-Host "`n[3/3] Starting native Next.js dev server (pnpm dev)..." -ForegroundColor Yellow
Write-Host @"

  Frontend : http://localhost:3000   (native, hot reload ~1s)
  API      : http://localhost:8080   (Podman, dotnet watch)
  Swagger  : http://localhost:8080/swagger
  Database : localhost:5433          (Podman, user oet_user)

  Edit anything under app/ components/ lib/ etc. -> browser updates in ~1s.
  Press Ctrl+C to stop the frontend (containers keep running).
  Stop containers later with:  podman compose -f docker-compose.hotreload.yml down

"@ -ForegroundColor White

pnpm run dev
