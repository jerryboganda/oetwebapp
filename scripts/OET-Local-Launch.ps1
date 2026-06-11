param([switch]$SkipBrowser)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $projectRoot '.local-deploy'
$logDir = Join-Path $localRoot 'logs'
$runnerDir = Join-Path $localRoot 'runners'
$storageDir = Join-Path $localRoot 'storage'
$pgBin = "$env:USERPROFILE\scoop\apps\postgresql\current\bin"
$pgData = "$env:USERPROFILE\scoop\persist\postgresql\data"
$databaseName = 'oetwebapp_local'
$env:PGPASSWORD = 'postgres'

function Write-Step([string]$Message) {
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Wait-Http([string]$Url, [int]$TimeoutSeconds) {
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    try {
      $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
      if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
        Write-Host "OK  $Url" -ForegroundColor Green
        return
      }
    } catch {}
    Start-Sleep -Seconds 3
  } while ((Get-Date) -lt $deadline)
  throw "$Url did not become healthy within $TimeoutSeconds seconds."
}

function Test-Postgres {
  & "$pgBin\pg_isready.exe" -h 127.0.0.1 -p 5432 -U postgres -t 1 2>$null | Out-Null
  return $LASTEXITCODE -eq 0
}

function Start-HiddenRunner([string]$Name, [string[]]$Lines, [string]$LogPath) {
  $runner = Join-Path $runnerDir "$Name.cmd"
  $Lines[$Lines.Count - 1] += " >> ""$LogPath"" 2>&1"
  Set-Content -LiteralPath $runner -Value ($Lines -join "`r`n") -Encoding ASCII
  Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c ""$runner""" -WorkingDirectory $projectRoot
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " OET Prep Platform - Localhost Launcher " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $logDir, $runnerDir, $storageDir | Out-Null

Write-Step 'Checking installed tools'
foreach ($command in @('node.exe', 'npm.cmd', 'dotnet.exe')) {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "$command was not found on PATH." }
}
foreach ($tool in @('pg_isready.exe', 'pg_ctl.exe', 'psql.exe')) {
  if (-not (Test-Path -LiteralPath (Join-Path $pgBin $tool))) { throw "$tool was not found under $pgBin." }
}
Write-Host 'OK  Node, npm, .NET, and Scoop PostgreSQL are available.' -ForegroundColor Green

Write-Step 'Starting PostgreSQL'
if (-not (Test-Postgres)) {
  if (-not (Test-Path -LiteralPath $pgData)) { throw "PostgreSQL data directory was not found: $pgData" }
  & "$pgBin\pg_ctl.exe" start -D $pgData -l (Join-Path $logDir 'postgres.log') | Out-Null
  $deadline = (Get-Date).AddSeconds(60)
  do {
    if (Test-Postgres) { break }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)
}
if (-not (Test-Postgres)) { throw 'PostgreSQL did not become ready on localhost:5432.' }
Write-Host 'OK  PostgreSQL is ready on localhost:5432.' -ForegroundColor Green

$dbExists = & "$pgBin\psql.exe" -h 127.0.0.1 -p 5432 -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$databaseName';"
if (($dbExists | Out-String).Trim() -ne '1') {
  & "$pgBin\psql.exe" -h 127.0.0.1 -p 5432 -U postgres -d postgres -c "CREATE DATABASE $databaseName;" | Out-Null
  Write-Host "OK  Created database $databaseName." -ForegroundColor Green
}

Write-Step 'Installing frontend dependencies when needed'
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'node_modules'))) {
  Push-Location $projectRoot
  try {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
  } finally {
    Pop-Location
  }
}
Write-Host 'OK  Frontend dependencies are available.' -ForegroundColor Green

Write-Step 'Starting backend API'
try { $apiReady = (Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5198/health' -TimeoutSec 3).StatusCode -lt 400 } catch { $apiReady = $false }
if (-not $apiReady) {
  $backendLog = Join-Path $logDir 'backend.log'
  Start-HiddenRunner 'backend-api' @(
    '@echo off',
    "cd /d ""$projectRoot""",
    'set ASPNETCORE_ENVIRONMENT=Development',
    'set Bootstrap__AutoMigrate=false',
    'set Bootstrap__SeedDemoData=false',
    "set ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=$databaseName;Username=postgres;Password=postgres",
    "set Storage__LocalRootPath=$storageDir",
    'dotnet run --project backend\src\OetLearner.Api\OetLearner.Api.csproj'
  ) $backendLog
}
Wait-Http 'http://localhost:5198/health' 300

Write-Step 'Starting frontend'
try { $webReady = (Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:3000' -TimeoutSec 3).StatusCode -lt 400 } catch { $webReady = $false }
if (-not $webReady) {
  $frontendLog = Join-Path $logDir 'frontend.log'
  Start-HiddenRunner 'frontend-web' @(
    '@echo off',
    "cd /d ""$projectRoot""",
    'set NEXT_PUBLIC_API_BASE_URL=http://localhost:5198',
    'set API_PROXY_TARGET_URL=http://127.0.0.1:5198',
    'set APP_URL=http://localhost:3000',
    'npm run dev'
  ) $frontendLog
}
Wait-Http 'http://localhost:3000' 240

Write-Host ""
Write-Host 'OET localhost stack is ready.' -ForegroundColor Green
Write-Host 'Web:       http://localhost:3000'
Write-Host 'API:       http://localhost:5198'
Write-Host 'Health:    http://localhost:5198/health'
Write-Host "Storage:   $storageDir"
Write-Host "Logs:      $logDir"

if (-not $SkipBrowser) { Start-Process 'http://localhost:3000' }
Write-Host 'This window will close in 8 seconds.' -ForegroundColor DarkGray
Start-Sleep -Seconds 8
