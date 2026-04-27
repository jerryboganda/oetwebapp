# Local Postgres setup for OET Web App (no Docker)
# Idempotent. Installs scoop + postgres if missing, starts the cluster,
# enforces password auth, and provisions the dev database.
$ErrorActionPreference = 'Stop'

if (-not (Get-Command scoop -ErrorAction SilentlyContinue)) {
    Write-Host '==> Installing scoop'
    Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
    Invoke-RestMethod -Uri https://get.scoop.sh -UseBasicParsing | Invoke-Expression
}

if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Host '==> Installing PostgreSQL via scoop'
    scoop install postgresql
}

$pgRoot = Join-Path $env:USERPROFILE 'scoop\apps\postgresql\current'
$pgData = Join-Path $pgRoot 'data'
$pgBin  = Join-Path $pgRoot 'bin'
$env:PATH = "$pgBin;$env:PATH"

if (-not (pg_isready -h localhost -p 5432 2>$null)) {
    Write-Host '==> Starting PostgreSQL'
    & "$pgBin\pg_ctl.exe" -D $pgData -l (Join-Path $pgData 'logfile') start | Out-Null
    Start-Sleep -Seconds 2
}

# Switch trust -> scram-sha-256 (idempotent: only if file still has 'trust')
$hba = Join-Path $pgData 'pg_hba.conf'
if ((Get-Content $hba -Raw) -match '\btrust\b') {
    Write-Host '==> Hardening pg_hba.conf to scram-sha-256'
    (Get-Content $hba) -replace '\btrust\b','scram-sha-256' | Set-Content $hba
    & "$pgBin\pg_ctl.exe" -D $pgData reload | Out-Null
}

# Set superuser password (will succeed via local socket if first run; otherwise password already set)
$env:PGPASSWORD = 'postgres'
try {
    & "$pgBin\psql.exe" -h localhost -U postgres -d postgres -c "ALTER USER postgres WITH PASSWORD 'postgres';" | Out-Null
} catch {
    Write-Host "Password already set or auth differs: $($_.Exception.Message)"
}

# Create dev database if missing
$exists = & "$pgBin\psql.exe" -h localhost -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='oet_learner_dev';"
if ($exists -ne '1') {
    Write-Host '==> Creating database oet_learner_dev'
    & "$pgBin\psql.exe" -h localhost -U postgres -d postgres -c 'CREATE DATABASE oet_learner_dev;' | Out-Null
}

# Register Windows service (best-effort, requires elevation)
if (-not (Get-Service -Name 'PostgreSQL' -ErrorAction SilentlyContinue)) {
    Write-Host '==> Registering Windows service (UAC prompt)'
    try {
        Start-Process -FilePath "$pgBin\pg_ctl.exe" `
            -ArgumentList @('register','-N','PostgreSQL','-D',$pgData,'-S','auto') `
            -Verb RunAs -Wait
    } catch {
        Write-Host "Service registration skipped: $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host 'PostgreSQL ready at localhost:5432'
Write-Host '  user:     postgres'
Write-Host '  password: postgres'
Write-Host '  database: oet_learner_dev'
Write-Host ''
Write-Host 'Connection string already configured in backend/src/OetLearner.Api/appsettings.Development.json'
Write-Host 'Run the backend with:  npm run backend:run'
