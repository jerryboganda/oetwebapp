param(
  [switch]$ValidateOnly,
  [switch]$SkipBrowser
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

trap {
  Write-Fail $_.Exception.Message
  Write-Host ""
  Write-Host "The launcher stopped before local deployment completed." -ForegroundColor Red
  Write-Host "Please send a screenshot of this window if it fails again."
  Write-Host "Press Enter to close this deployment window..."
  [void][Console]::ReadLine()
  break
}

function Write-Step {
  param([string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
  param([string]$Message)
  Write-Host "OK  $Message" -ForegroundColor Green
}

function Write-Warn {
  param([string]$Message)
  Write-Host "WARN $Message" -ForegroundColor Yellow
}

function Write-Fail {
  param([string]$Message)
  Write-Host "FAIL $Message" -ForegroundColor Red
}

function Test-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Admin {
  if (Test-Admin) {
    Write-Ok "Running with administrator privileges."
    return
  }

  Write-Step "Requesting administrator privileges"
  $argsList = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', "`"$PSCommandPath`""
  )
  if ($SkipBrowser) {
    $argsList += '-SkipBrowser'
  }
  if ($ValidateOnly) {
    $argsList += '-ValidateOnly'
  }

  Start-Process -FilePath 'powershell.exe' -ArgumentList $argsList -Verb RunAs
  exit
}

function Assert-Command {
  param(
    [string]$Name,
    [string]$InstallHint
  )

  $command = Get-Command $Name -ErrorAction SilentlyContinue
  if (-not $command) {
    throw "$Name was not found. $InstallHint"
  }
  Write-Ok "$Name found: $($command.Source)"
}

function Test-HttpOk {
  param(
    [string]$Url,
    [int]$TimeoutSeconds = 3
  )

  try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec $TimeoutSeconds
    return ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400)
  } catch {
    return $false
  }
}

function Wait-HttpOk {
  param(
    [string]$Url,
    [int]$TimeoutSeconds = 90
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    if (Test-HttpOk -Url $Url -TimeoutSeconds 5) {
      Write-Ok "$Url is responding."
      return
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)

  throw "$Url did not become healthy within $TimeoutSeconds seconds."
}

function Set-EnvFileValue {
  param(
    [string]$Path,
    [string]$Key,
    [string]$Value
  )

  $line = "$Key=$Value"
  if (-not (Test-Path -LiteralPath $Path)) {
    Set-Content -LiteralPath $Path -Value $line -Encoding UTF8
    return
  }

  $content = Get-Content -LiteralPath $Path
  $pattern = "^\s*$([regex]::Escape($Key))\s*="
  $found = $false
  $updated = foreach ($existingLine in $content) {
    if ($existingLine -match $pattern) {
      $found = $true
      $line
    } else {
      $existingLine
    }
  }

  if (-not $found) {
    $updated += $line
  }

  Set-Content -LiteralPath $Path -Value $updated -Encoding UTF8
}

function Find-PostgresTool {
  param([string]$ToolName)

  $command = Get-Command $ToolName -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }

  $candidates = @(
    "$env:USERPROFILE\scoop\apps\postgresql\current\bin\$ToolName.exe",
    "$env:ProgramFiles\PostgreSQL\17\bin\$ToolName.exe",
    "$env:ProgramFiles\PostgreSQL\16\bin\$ToolName.exe",
    "$env:ProgramFiles\PostgreSQL\15\bin\$ToolName.exe"
  )

  foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
      return $candidate
    }
  }

  return $null
}

function Join-ProcessArguments {
  param([string[]]$Arguments)

  return ($Arguments | ForEach-Object {
    if ($_ -match '[\s"]') {
      '"' + ($_ -replace '"', '\"') + '"'
    } else {
      $_
    }
  }) -join ' '
}

function Test-PostgresReady {
  param(
    [string]$PsqlPath,
    [string]$PgIsReadyPath
  )

  $hostsToTry = @('127.0.0.1', 'localhost')

  foreach ($hostName in $hostsToTry) {
    if ($PgIsReadyPath) {
      & $PgIsReadyPath -h $hostName -p 5432 -U postgres | Out-Null
      if ($LASTEXITCODE -eq 0) {
        return $true
      }
    }

    if ($PsqlPath) {
      $result = & $PsqlPath -h $hostName -p 5432 -U postgres -d postgres -tAc "SELECT 1;" 2>$null
      if ($LASTEXITCODE -eq 0 -and (($result | Out-String).Trim()) -eq '1') {
        return $true
      }
    }
  }

  return $false
}

function Wait-PostgresReady {
  param(
    [string]$PsqlPath,
    [string]$PgIsReadyPath,
    [int]$TimeoutSeconds = 45
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    if (Test-PostgresReady -PsqlPath $PsqlPath -PgIsReadyPath $PgIsReadyPath) {
      return $true
    }
    Start-Sleep -Seconds 1
  } while ((Get-Date) -lt $deadline)

  return $false
}

function Ensure-Postgres {
  Write-Step "Checking PostgreSQL"

  $pgIsReady = Find-PostgresTool -ToolName 'pg_isready'
  $pgCtl = Find-PostgresTool -ToolName 'pg_ctl'
  $postgresExe = Find-PostgresTool -ToolName 'postgres'
  $psql = Find-PostgresTool -ToolName 'psql'

  if (-not $psql) {
    throw "psql.exe was not found. Install PostgreSQL client tools or add PostgreSQL bin to PATH."
  }

  $env:PGPASSWORD = 'postgres'

  $isReady = Test-PostgresReady -PsqlPath $psql -PgIsReadyPath $pgIsReady

  if (-not $isReady) {
    $tcpListener = Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($tcpListener) {
      Write-Warn "A PostgreSQL/server listener already exists on port 5432. Waiting for it to become ready instead of starting another server."
      if (Wait-PostgresReady -PsqlPath $psql -PgIsReadyPath $pgIsReady -TimeoutSeconds 60) {
        Write-Ok "PostgreSQL is already running on port 5432."
        $isReady = $true
      } else {
        throw "Port 5432 is already in use, but PostgreSQL did not accept the configured local credentials."
      }
    }
  }

  if (-not $isReady) {
    Write-Warn "PostgreSQL is not responding on localhost:5432. Trying to start it."

    $service = Get-Service | Where-Object {
      $_.Name -like 'postgresql*' -or $_.DisplayName -like 'postgresql*'
    } | Select-Object -First 1

    if ($service) {
      if ($service.Status -ne 'Running') {
        Start-Service -Name $service.Name
      }
      Write-Ok "PostgreSQL service is running: $($service.Name)"
    } elseif ($postgresExe -or $pgCtl) {
      $dataDirCandidates = @(
        "$env:USERPROFILE\scoop\persist\postgresql\data",
        "$env:ProgramFiles\PostgreSQL\17\data",
        "$env:ProgramFiles\PostgreSQL\16\data",
        "$env:ProgramFiles\PostgreSQL\15\data"
      )
      $dataDir = $dataDirCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
      if (-not $dataDir) {
        throw "PostgreSQL data directory was not found. Checked: $($dataDirCandidates -join ', ')"
      }

      $pgLogDir = Join-Path $PSScriptRoot '..\.local-deploy\logs'
      New-Item -ItemType Directory -Force -Path $pgLogDir | Out-Null

      if ($postgresExe) {
        $pgStdout = Join-Path $pgLogDir 'postgres.stdout.log'
        $pgStderr = Join-Path $pgLogDir 'postgres.stderr.log'
        $pgArgs = Join-ProcessArguments -Arguments @('-D', $dataDir)
        Start-Process -WindowStyle Hidden -FilePath $postgresExe -ArgumentList $pgArgs -RedirectStandardOutput $pgStdout -RedirectStandardError $pgStderr | Out-Null
        if (-not (Wait-PostgresReady -PsqlPath $psql -PgIsReadyPath $pgIsReady -TimeoutSeconds 75)) {
          throw "postgres.exe was started, but PostgreSQL did not become ready. See $pgStderr"
        }
      } else {
        $pgLog = Join-Path $pgLogDir 'postgres.log'
        $pgArgs = Join-ProcessArguments -Arguments @('start', '-D', $dataDir, '-l', $pgLog)
        Start-Process -WindowStyle Hidden -FilePath $pgCtl -ArgumentList $pgArgs | Out-Null
        if (-not (Wait-PostgresReady -PsqlPath $psql -PgIsReadyPath $pgIsReady -TimeoutSeconds 75)) {
          throw "pg_ctl was started, but PostgreSQL did not become ready. See $pgLog"
        }
      }
      Write-Ok "PostgreSQL started."
    } else {
      throw "PostgreSQL is not running, and neither a PostgreSQL service nor postgres.exe/pg_ctl.exe was found."
    }
  } else {
    Write-Ok "PostgreSQL is responding on localhost:5432."
  }

  if (-not (Wait-PostgresReady -PsqlPath $psql -PgIsReadyPath $pgIsReady -TimeoutSeconds 30)) {
    throw "PostgreSQL did not become ready on localhost:5432."
  }

  $dbExists = & $psql -h 127.0.0.1 -p 5432 -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='oet_learner_dev';"
  if (($dbExists | Out-String).Trim() -ne '1') {
    Write-Warn "Database oet_learner_dev does not exist. Creating it."
    & $psql -h 127.0.0.1 -p 5432 -U postgres -c "CREATE DATABASE oet_learner_dev;" | Out-Host
  }
  Write-Ok "Database oet_learner_dev is available."
}

function Start-LocalProcess {
  param(
    [string]$Name,
    [string]$Command,
    [string]$LogPath,
    [string]$WorkingDirectory
  )

  $runnerDir = Join-Path $WorkingDirectory '.local-deploy\runners'
  New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
  $safeName = ($Name -replace '[^a-zA-Z0-9_-]', '-').ToLowerInvariant()
  $runnerPath = Join-Path $runnerDir "$safeName.cmd"
  $commandForCmd = $Command
  if ($commandForCmd.StartsWith('cmd /c ')) {
    $commandForCmd = $commandForCmd.Substring(7)
  }

  $runnerContent = @(
    '@echo off',
    "cd /d ""$WorkingDirectory""",
    'set ASPNETCORE_ENVIRONMENT=Development',
    'set API_PROXY_TARGET_URL=http://127.0.0.1:5198',
    'set APP_URL=http://localhost:3000',
    'set NEXT_PUBLIC_API_BASE_URL=',
    "$commandForCmd > ""$LogPath"" 2>&1"
  )
  Set-Content -LiteralPath $runnerPath -Value $runnerContent -Encoding ASCII

  Start-Process -WindowStyle Hidden -FilePath 'cmd.exe' -ArgumentList "/c ""$runnerPath""" -WorkingDirectory $WorkingDirectory | Out-Null

  Write-Ok "$Name start command launched. Log: $LogPath"
}

function Ensure-NodeDependencies {
  param([string]$ProjectRoot)

  Write-Step "Checking Node dependencies"
  if (Test-Path -LiteralPath (Join-Path $ProjectRoot 'node_modules')) {
    Write-Ok "node_modules already exists."
    return
  }

  Write-Warn "node_modules is missing. Installing from package-lock.json with npm ci."
  Push-Location $ProjectRoot
  try {
    cmd /c npm ci
    if ($LASTEXITCODE -ne 0) {
      throw "npm ci failed with exit code $LASTEXITCODE."
    }
  } finally {
    Pop-Location
  }
}

function Test-Login {
  Write-Step "Testing local learner sign-in through the frontend proxy"
  $body = @{
    email = 'learner@oet-prep.dev'
    password = 'Password123!'
    rememberMe = $true
  } | ConvertTo-Json

  try {
    $response = Invoke-RestMethod -Method Post -Uri 'http://localhost:3000/api/backend/v1/auth/sign-in' -ContentType 'application/json' -Body $body -TimeoutSec 20
    if ($response.accessToken -and $response.currentUser.email -eq 'learner@oet-prep.dev') {
      Write-Ok "Learner sign-in works through http://localhost:3000/api/backend."
      return
    }
  } catch {
    throw "Learner sign-in test failed: $($_.Exception.Message)"
  }

  throw "Learner sign-in test did not return a valid session."
}

function Show-IntegrationSummary {
  param([string]$ProjectRoot)

  Write-Step "Checking local integration configuration"
  $appSettingsPath = Join-Path $ProjectRoot 'backend\src\OetLearner.Api\appsettings.Development.json'
  if (-not (Test-Path -LiteralPath $appSettingsPath)) {
    Write-Warn "Could not find appsettings.Development.json for optional integration checks."
    return
  }

  $settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json

  if ($settings.Bootstrap.AutoMigrate -eq $true) {
    Write-Ok "Backend auto-migrations are enabled for local deployment."
  } else {
    Write-Warn "Backend auto-migrations are not enabled in Development settings."
  }

  if ($settings.Bootstrap.SeedDemoData -eq $true) {
    Write-Ok "Demo data seeding is enabled for local users and test content."
  } else {
    Write-Warn "Demo data seeding is not enabled; local test users may be missing."
  }

  if ($settings.Billing.AllowSandboxFallbacks -eq $true) {
    Write-Ok "Billing sandbox fallbacks are enabled for localhost."
  } else {
    Write-Warn "Billing sandbox fallbacks are disabled; Stripe/payment keys may be required."
  }

  $externalAuth = $settings.ExternalAuth
  foreach ($providerName in @('Google', 'Facebook', 'LinkedIn')) {
    $provider = $externalAuth.$providerName
    if ($provider -and $provider.Enabled -eq $true -and $provider.ClientId -and $provider.ClientSecret) {
      Write-Ok "$providerName external auth is configured."
    } else {
      Write-Warn "$providerName external auth is disabled or missing local keys. Core username/password login still works."
    }
  }

  if ($env:DO_INFERENCE_API_KEY -or (Select-String -LiteralPath (Join-Path $ProjectRoot '.env.local') -Pattern '^DO_INFERENCE_API_KEY=' -Quiet -ErrorAction SilentlyContinue)) {
    Write-Ok "DigitalOcean inference key is present in the local environment file or process environment."
  } else {
    Write-Warn "DigitalOcean inference key was not found in .env.local/process env; AI provider calls may need configuration."
  }
}

$projectRoot = 'C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App'
$desktopPath = [Environment]::GetFolderPath('Desktop')
$logDir = Join-Path $projectRoot '.local-deploy\logs'
$envFile = Join-Path $projectRoot '.env.local'
$deployLog = Join-Path $logDir 'one-click-deploy.log'

Write-Host "OIT with Dr. Hisham project - one-click local deployment" -ForegroundColor Magenta
Write-Host "Project: $projectRoot"

Ensure-Admin

if (-not (Test-Path -LiteralPath $projectRoot)) {
  throw "Project folder was not found: $projectRoot"
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
try {
  Start-Transcript -LiteralPath $deployLog -Append | Out-Null
} catch {
  Write-Warn "Could not start transcript log: $($_.Exception.Message)"
}

Write-Step "Checking required tools"
Assert-Command -Name 'node' -InstallHint 'Install Node.js and retry.'
Assert-Command -Name 'npm' -InstallHint 'Install npm and retry.'
Assert-Command -Name 'dotnet' -InstallHint 'Install the .NET SDK and retry.'

if ($ValidateOnly) {
  Write-Ok "Script validation completed. No services were started because -ValidateOnly was used."
  exit 0
}

Write-Step "Preparing local environment"
Set-EnvFileValue -Path $envFile -Key 'APP_URL' -Value 'http://localhost:3000'
Set-EnvFileValue -Path $envFile -Key 'CAPACITOR_APP_URL' -Value 'http://localhost:3000'
Set-EnvFileValue -Path $envFile -Key 'API_PROXY_TARGET_URL' -Value 'http://127.0.0.1:5198'
Write-Ok ".env.local has local app/proxy values."

Ensure-Postgres
Ensure-NodeDependencies -ProjectRoot $projectRoot

Write-Step "Starting backend API"
if (Test-HttpOk -Url 'http://localhost:5198/health' -TimeoutSeconds 5) {
  Write-Ok "Backend is already running on http://localhost:5198."
} else {
  Start-LocalProcess -Name 'Backend API' -Command 'cmd /c npm run backend:run' -LogPath (Join-Path $logDir 'backend.log') -WorkingDirectory $projectRoot
}
Wait-HttpOk -Url 'http://localhost:5198/health' -TimeoutSeconds 180

Write-Step "Starting frontend"
if (Test-HttpOk -Url 'http://localhost:3000/sign-in' -TimeoutSeconds 5) {
  Write-Ok "Frontend is already running on http://localhost:3000."
} else {
  Start-LocalProcess -Name 'Frontend web app' -Command 'cmd /c npm run dev' -LogPath (Join-Path $logDir 'frontend.log') -WorkingDirectory $projectRoot
}
Wait-HttpOk -Url 'http://localhost:3000/sign-in' -TimeoutSeconds 180

Test-Login
Show-IntegrationSummary -ProjectRoot $projectRoot

Write-Step "Local deployment is ready"
Write-Host ""
Write-Host "URLs" -ForegroundColor Cyan
Write-Host "  Web app:        http://localhost:3000"
Write-Host "  Sign in:        http://localhost:3000/sign-in"
Write-Host "  API:            http://localhost:5198"
Write-Host "  API health:     http://localhost:5198/health"
Write-Host "  API ready:      http://localhost:5198/health/ready"
Write-Host "  API live:       http://localhost:5198/health/live"
Write-Host ""
Write-Host "Test users" -ForegroundColor Cyan
Write-Host "  Learner:            learner@oet-prep.dev / Password123!"
Write-Host "  Expert:             expert@oet-prep.dev / Password123!"
Write-Host "  Secondary expert:   expert-unauthorised@oet-prep.dev / Password123!"
Write-Host "  Admin:              admin@oet-prep.dev / Password123!"
Write-Host ""
Write-Host "Logs" -ForegroundColor Cyan
Write-Host "  $logDir"
Write-Host ""

if (-not $SkipBrowser) {
  Start-Process 'http://localhost:3000/sign-in'
}

Write-Host "Deployment completed. This window will close automatically in 8 seconds..." -ForegroundColor Green
try {
  Stop-Transcript | Out-Null
} catch {
  # Ignore transcript shutdown failures.
}
Start-Sleep -Seconds 8
