# OET-Launch.ps1 - Start the full OET Prep Platform locally
# Starts PostgreSQL, backend API, and frontend dev server.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Derive the repo root from this script's location so the launcher is portable
# across machines (scripts/ lives directly under the repo root).
$projectRoot = Split-Path -Parent $PSScriptRoot
$pgBin = 'C:\Program Files\PostgreSQL\17\bin'
$pgService = 'postgresql-17'
$logDir = Join-Path $projectRoot '.local-deploy\logs'

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   OET Prep Platform - Local Launcher  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# --- 1. Start PostgreSQL ---
Write-Host "[1/4] Starting PostgreSQL..." -ForegroundColor Yellow

$env:PGPASSWORD = 'postgres'
$env:PATH = "$pgBin;$env:PATH"

if (-not (Test-Path "$pgBin\pg_isready.exe")) {
    Write-Host "  FAILED: pg_isready.exe not found at $pgBin" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

$pgReady = $false
try {
    & "$pgBin\pg_isready.exe" -h localhost -p 5432 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $pgReady = $true }
} catch {}

if (-not $pgReady) {
    # PostgreSQL is installed as the Windows service 'postgresql-17' on this
    # machine. Start it via the service control manager and wait for readiness.
    $svc = Get-Service -Name $pgService -ErrorAction SilentlyContinue
    if ($null -eq $svc) {
        Write-Host "  FAILED: Windows service '$pgService' not found." -ForegroundColor Red
        Read-Host "Press Enter to close"
        exit 1
    }
    if ($svc.Status -ne 'Running') {
        try {
            Start-Service -Name $pgService -ErrorAction Stop
            Write-Host "       Started Windows service '$pgService'" -ForegroundColor DarkGray
        } catch {
            Write-Host "  FAILED: Could not start service '$pgService': $($_.Exception.Message)" -ForegroundColor Red
            Read-Host "Press Enter to close"
            exit 1
        }
    }

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        try {
            & "$pgBin\pg_isready.exe" -h localhost -p 5432 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $pgReady = $true; break }
        } catch {}
    }

    if (-not $pgReady) {
        Write-Host "  FAILED: PostgreSQL service '$pgService' did not become ready." -ForegroundColor Red
        Read-Host "Press Enter to close"
        exit 1
    }
}

Write-Host "       PostgreSQL is running on localhost:5432" -ForegroundColor Green

try {
    $dbExists = & "$pgBin\psql.exe" -h 127.0.0.1 -p 5432 -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='oet_learner_dev';" 2>$null
    if (($dbExists | Out-String).Trim() -ne '1') {
        & "$pgBin\psql.exe" -h 127.0.0.1 -p 5432 -U postgres -c "CREATE DATABASE oet_learner_dev;" 2>$null | Out-Null
        Write-Host "       Created database oet_learner_dev" -ForegroundColor Green
    }
} catch {}

# --- 2. Start Backend API ---
Write-Host "[2/4] Starting Backend API..." -ForegroundColor Yellow
Write-Host "       (First launch compiles .NET - may take 2-4 minutes)" -ForegroundColor DarkGray

$backendAlive = $false
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5198/health' -TimeoutSec 3 -ErrorAction SilentlyContinue
    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) { $backendAlive = $true }
} catch {}

if (-not $backendAlive) {
    $envLocal = Join-Path $projectRoot '.env.local'
    $aiEnvVars = @{}
    if (Test-Path $envLocal) {
        Get-Content $envLocal | ForEach-Object {
            if ($_ -match '^\s*(AI__[^=]+)\s*=\s*(.*)$') {
                $aiEnvVars[$Matches[1]] = $Matches[2]
            }
        }
    }

    $runnerDir = Join-Path $projectRoot '.local-deploy\runners'
    New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
    $backendLog = Join-Path $logDir 'backend.log'
    Remove-Item $backendLog -Force -ErrorAction SilentlyContinue

    $runnerLines = [System.Collections.ArrayList]@()
    [void]$runnerLines.Add('@echo off')
    [void]$runnerLines.Add("cd /d ""$projectRoot""")
    [void]$runnerLines.Add('set ASPNETCORE_ENVIRONMENT=Development')
    [void]$runnerLines.Add('set PGPASSWORD=postgres')
    [void]$runnerLines.Add('set Bootstrap__SeedDemoData=false')
    foreach ($key in $aiEnvVars.Keys) {
        [void]$runnerLines.Add("set $key=$($aiEnvVars[$key])")
    }
    [void]$runnerLines.Add("npm run backend:run > ""$backendLog"" 2>&1")

    $runnerPath = Join-Path $runnerDir 'backend-api.cmd'
    Set-Content -LiteralPath $runnerPath -Value ($runnerLines -join "`r`n") -Encoding ASCII
    Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c ""$runnerPath""" -WorkingDirectory $projectRoot

    # Wait up to 5 MINUTES (300s) - .NET build can take 2+ min on first run
    $deadline = (Get-Date).AddSeconds(300)
    $lastStatus = ''
    $dotCount = 0
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        try {
            $resp = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5198/health' -TimeoutSec 3 -ErrorAction SilentlyContinue
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) { $backendAlive = $true; break }
        } catch {}

        $dotCount++
        if (($dotCount % 5) -eq 0 -and (Test-Path $backendLog)) {
            $logTail = Get-Content $backendLog -Tail 1 -ErrorAction SilentlyContinue
            if ($logTail -and $logTail -ne $lastStatus) {
                $lastStatus = $logTail
                $short = if ($logTail.Length -gt 70) { $logTail.Substring(0, 67) + '...' } else { $logTail }
                Write-Host "       ... $short" -ForegroundColor DarkGray
            }
        } else {
            Write-Host -NoNewline "." -ForegroundColor DarkGray
        }
    }
    if (-not $backendAlive) { Write-Host "" }

    if (-not $backendAlive) {
        Write-Host "  FAILED: Backend did not start within 5 minutes." -ForegroundColor Red
        Write-Host "  Log: $backendLog" -ForegroundColor Red
        if (Test-Path $backendLog) {
            Get-Content $backendLog -Tail 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
        Read-Host "Press Enter to close"
        exit 1
    }
}

Write-Host ""
Write-Host "       Backend API running at http://localhost:5198" -ForegroundColor Green

# --- 3. Start Frontend ---
Write-Host "[3/4] Starting Frontend..." -ForegroundColor Yellow
Write-Host "       (First launch compiles Next.js - may take 1-2 minutes)" -ForegroundColor DarkGray

$frontendAlive = $false
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:3000' -TimeoutSec 3 -ErrorAction SilentlyContinue
    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) { $frontendAlive = $true }
} catch {}

if (-not $frontendAlive) {
    $runnerDir = Join-Path $projectRoot '.local-deploy\runners'
    New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
    $frontendLog = Join-Path $logDir 'frontend.log'
    Remove-Item $frontendLog -Force -ErrorAction SilentlyContinue

    $fRunnerLines = [System.Collections.ArrayList]@()
    [void]$fRunnerLines.Add('@echo off')
    [void]$fRunnerLines.Add("cd /d ""$projectRoot""")
    [void]$fRunnerLines.Add('set NEXT_PUBLIC_API_BASE_URL=http://localhost:5198')
    [void]$fRunnerLines.Add('set APP_URL=http://localhost:3000')
    [void]$fRunnerLines.Add('set API_PROXY_TARGET_URL=http://127.0.0.1:5198')
    [void]$fRunnerLines.Add("npx next dev --turbopack > ""$frontendLog"" 2>&1")

    $runnerPath = Join-Path $runnerDir 'frontend-web.cmd'
    Set-Content -LiteralPath $runnerPath -Value ($fRunnerLines -join "`r`n") -Encoding ASCII
    Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c ""$runnerPath""" -WorkingDirectory $projectRoot

    $deadline = (Get-Date).AddSeconds(180)
    $dotCount = 0
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        try {
            $resp = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:3000' -TimeoutSec 3 -ErrorAction SilentlyContinue
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) { $frontendAlive = $true; break }
        } catch {}
        $dotCount++
        if (($dotCount % 5) -eq 0 -and (Test-Path $frontendLog)) {
            $logTail = Get-Content $frontendLog -Tail 1 -ErrorAction SilentlyContinue
            if ($logTail) {
                $short = if ($logTail.Length -gt 70) { $logTail.Substring(0, 67) + '...' } else { $logTail }
                Write-Host "       ... $short" -ForegroundColor DarkGray
            }
        } else {
            Write-Host -NoNewline "." -ForegroundColor DarkGray
        }
    }
    if (-not $frontendAlive) { Write-Host "" }

    if (-not $frontendAlive) {
        Write-Host "  FAILED: Frontend did not start within 3 minutes." -ForegroundColor Red
        Write-Host "  Log: $frontendLog" -ForegroundColor Red
        if (Test-Path $frontendLog) {
            Get-Content $frontendLog -Tail 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
        Read-Host "Press Enter to close"
        exit 1
    }
}

Write-Host ""
Write-Host "       Frontend running at http://localhost:3000" -ForegroundColor Green

# --- 4. Open Browser ---
Write-Host "[4/4] Opening browser..." -ForegroundColor Yellow
Start-Process 'http://localhost:3000/sign-in'

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "   OET Platform is LIVE!               " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Frontend:  http://localhost:3000" -ForegroundColor White
Write-Host "  Backend:   http://localhost:5198" -ForegroundColor White
Write-Host "  Database:  localhost:5432/oet_learner_dev" -ForegroundColor White
Write-Host ""
Write-Host "  To stop: Double-click 'Stop OET App' on desktop" -ForegroundColor DarkGray
Write-Host ""
Write-Host "This window will close in 10 seconds..." -ForegroundColor DarkGray
Start-Sleep -Seconds 10