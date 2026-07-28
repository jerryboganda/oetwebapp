#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the full local dev stack (tunnel + API + web + desktop) against the LIVE production database.

.DESCRIPTION
    Opens one PowerShell window per long-running service so they survive independently of any
    Claude Code / editor session:

      1. SSH tunnel  127.0.0.1:15433 -> oet-postgres (172.20.0.3:5432) on the prod VPS, in a
         self-healing reconnect loop.
      2. .NET API    http://localhost:5198, pointed at the prod DB through the tunnel.
      3. Next.js     http://localhost:3000, with its API proxy overridden to the local API
                     (.env.development.local pins a dead Podman port, so we override at launch).
      4. Tauri shell remote-only desktop client loaded from http://localhost:3000.
      5. Mobile view http://localhost:3001 — a static phone-frame page (scripts/mobile-view)
                     that iframes localhost:3000, standing in for the Capacitor webview
                     (no Android emulator is installed on this host).

    Nothing here is committed with a secret: the Postgres password is resolved at runtime
    (env var -> local untracked file -> fetched over SSH from the VPS).

.PARAMETER SkipDesktop
    Don't build/launch the Tauri desktop shell (first cargo build takes ~7 minutes).

.PARAMETER SkipWeb
    Don't launch the Next.js dev server (use if you already have one running).

.PARAMETER SkipMobile
    Don't launch the phone-frame mobile view.

.PARAMETER Stop
    Stop everything this script started (tunnel, API, web, desktop, mobile view) and exit.

.EXAMPLE
    pwsh -File scripts/start-local-prod.ps1
    pwsh -File scripts/start-local-prod.ps1 -SkipDesktop
    pwsh -File scripts/start-local-prod.ps1 -Stop

.NOTES
    !! THIS WRITES TO PRODUCTION DATA !!
    The API boots in the Development environment against the live DB, which means:
      - startup MigrateAsync applies any PENDING MIGRATION in your working tree to PRODUCTION;
      - dev seeders run against production on every boot;
      - every local click (test users, exam attempts, credit deductions, deletes) is real.
    Safe for read-mostly UI work. Before touching a migration or a destructive admin action,
    switch to a local Postgres restored from a prod dump instead.
#>
[CmdletBinding()]
param(
    [switch]$SkipDesktop,
    [switch]$SkipWeb,
    [switch]$SkipMobile,
    [switch]$Stop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- configuration
$RepoRoot     = Split-Path -Parent $PSScriptRoot
$ApiDir       = Join-Path $RepoRoot 'backend\src\OetLearner.Api'
$DpKeysDir    = Join-Path $ApiDir  'App_Data\storage\.data-protection'

$VpsHost      = 'root@185.252.233.186'
$PgContainerIp= '172.20.0.3'
$TunnelPort   = 15433
$ApiPort      = 5198
$WebPort      = 3000
$MobilePort   = 3001
$WebUrl       = "http://localhost:$WebPort"
$ApiUrl       = "http://localhost:$ApiPort"
$MobileDir    = Join-Path $PSScriptRoot 'mobile-view'

$DbName       = 'oet_learner'
$DbUser       = 'oet_learner'
$PasswordFile = Join-Path $RepoRoot 'config\local-prod-db.password'   # untracked; optional

function Write-Step { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "    $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    $Message" -ForegroundColor Yellow }

# ------------------------------------------------------------------------ -Stop
if ($Stop) {
    Write-Step 'Stopping local prod stack'
    Get-Process oet-desktop -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    # Only kill ssh processes that hold our forward, not the user's other sessions.
    Get-CimInstance Win32_Process -Filter "Name = 'ssh.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -match "${TunnelPort}:${PgContainerIp}" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    foreach ($port in @($ApiPort, $WebPort, $MobilePort)) {
        Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique |
            ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
    }
    Write-Ok 'Stopped tunnel, API, web, desktop and mobile view (windows may need closing manually).'
    return
}

# ------------------------------------------------------------------ preflight
Write-Step 'Preflight'

foreach ($tool in @('ssh', 'dotnet', 'pnpm')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' was not found on PATH. Install it (or open a shell where it resolves) and re-run."
    }
}

if (-not (Test-Path $DpKeysDir) -or -not (Get-ChildItem $DpKeysDir -Filter 'key-*.xml' -ErrorAction SilentlyContinue)) {
    Write-Warn "No DataProtection keys in $DpKeysDir"
    Write-Warn 'The API will fail to decrypt prod runtime settings. Copy them from the live container:'
    Write-Warn "  ssh $VpsHost `"docker cp oet-api-green:/var/opt/oet-learner/storage/.data-protection/. /tmp/dp && tar -C /tmp/dp -cf - .`" | tar -C `"$DpKeysDir`" -xf -"
} else {
    Write-Ok 'DataProtection keys present.'
}

# Stray dotnet.exe holds a lock on the build output and makes the next `dotnet run` hang.
$strays = @(Get-Process dotnet, VBCSCompiler -ErrorAction SilentlyContinue)
if ($strays.Count -gt 0) {
    Write-Warn "Killing $($strays.Count) stray dotnet/VBCSCompiler process(es) holding build locks."
    $strays | Stop-Process -Force -ErrorAction SilentlyContinue
}

# --------------------------------------------------------- resolve DB password
Write-Step 'Resolving production DB password'
$DbPassword = $null

if ($env:OET_PROD_DB_PASSWORD) {
    $DbPassword = $env:OET_PROD_DB_PASSWORD
    Write-Ok 'Using $env:OET_PROD_DB_PASSWORD.'
} elseif (Test-Path $PasswordFile) {
    $DbPassword = (Get-Content $PasswordFile -Raw).Trim()
    Write-Ok "Using $PasswordFile."
} else {
    Write-Ok 'Fetching from the VPS over SSH (not stored on disk).'
    $line = ssh -o BatchMode=yes -o ConnectTimeout=15 $VpsHost "grep -E '^POSTGRES_PASSWORD=' /opt/oetwebapp/.env.production"
    if ($LASTEXITCODE -ne 0 -or -not $line) {
        throw "Could not read POSTGRES_PASSWORD from the VPS. Set `$env:OET_PROD_DB_PASSWORD or create $PasswordFile."
    }
    $DbPassword = ($line -replace '^POSTGRES_PASSWORD=', '').Trim()
}

if (-not $DbPassword) { throw 'Resolved an empty DB password; aborting.' }
$ConnString = "Host=127.0.0.1;Port=$TunnelPort;Database=$DbName;Username=$DbUser;Password=$DbPassword"

# ------------------------------------------------------------------- helpers
function Start-ServiceWindow {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Body,          # PowerShell to run inside the new window
        [hashtable]$Environment = @{}
    )
    $prelude = "`$Host.UI.RawUI.WindowTitle = '$Title'`n"
    foreach ($key in $Environment.Keys) {
        $value = ($Environment[$key] -replace "'", "''")
        $prelude += "`$env:$key = '$value'`n"
    }
    Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ($prelude + $Body) `
        -WorkingDirectory $RepoRoot | Out-Null
}

function Wait-ForPort {
    param([int]$Port, [int]$TimeoutSeconds = 120, [string]$Label = "port $Port")
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { return $true }
        Start-Sleep -Seconds 2
    }
    Write-Warn "Timed out after ${TimeoutSeconds}s waiting for $Label."
    return $false
}

function Wait-ForHttp {
    param([string]$Url, [int]$TimeoutSeconds = 300, [string]$Label = 'service')
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 | Out-Null
            return $true
        } catch {
            # A 3xx/4xx still proves something is listening and responding.
            if ($_.Exception.Response) { return $true }
        }
        Start-Sleep -Seconds 3
    }
    Write-Warn "Timed out after ${TimeoutSeconds}s waiting for $Label ($Url)."
    return $false
}

# ------------------------------------------------------------------ 1. tunnel
Write-Step "SSH tunnel 127.0.0.1:$TunnelPort -> ${PgContainerIp}:5432"
if (Get-NetTCPConnection -State Listen -LocalPort $TunnelPort -ErrorAction SilentlyContinue) {
    Write-Ok 'Already listening; reusing it.'
} else {
    # Self-healing: a dropped forward reconnects in ~5s instead of taking the API down for good.
    $tunnelBody = @"
while (`$true) {
  Write-Host '[tunnel] connecting...' -ForegroundColor Cyan
  ssh -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 ``
      -o TCPKeepAlive=yes -o ConnectTimeout=15 ``
      -L ${TunnelPort}:${PgContainerIp}:5432 $VpsHost
  Write-Host '[tunnel] dropped; reconnecting in 5s' -ForegroundColor Yellow
  Start-Sleep -Seconds 5
}
"@
    Start-ServiceWindow -Title 'OET tunnel -> prod postgres' -Body $tunnelBody
    if (Wait-ForPort -Port $TunnelPort -TimeoutSeconds 60 -Label 'SSH tunnel') { Write-Ok 'Tunnel up.' }
}

# --------------------------------------------------------------------- 2. API
Write-Step "API on $ApiUrl (LIVE production database)"
if (Get-NetTCPConnection -State Listen -LocalPort $ApiPort -ErrorAction SilentlyContinue) {
    Write-Ok 'Already listening; reusing it.'
} else {
    # Auto-restart: unlike the tunnel, the API exits when the DB disappears.
    $apiBody = @"
Set-Location '$ApiDir'
while (`$true) {
  Write-Host '[api] starting...' -ForegroundColor Cyan
  dotnet run --no-launch-profile
  Write-Host '[api] exited; restarting in 5s (Ctrl+C twice to stop)' -ForegroundColor Yellow
  Start-Sleep -Seconds 5
}
"@
    Start-ServiceWindow -Title 'OET API :5198 (PROD DB)' -Body $apiBody -Environment @{
        ConnectionStrings__DefaultConnection = $ConnString
        ASPNETCORE_URLS                      = $ApiUrl
        ASPNETCORE_ENVIRONMENT               = 'Development'
    }
    # Killing stray dotnet.exe above invalidates the build output, so a cold rebuild is common here.
    Write-Ok 'Starting (a cold rebuild can take 10+ minutes)...'
    if (Wait-ForHttp -Url "$ApiUrl/health" -TimeoutSeconds 900 -Label 'API health') {
        Write-Ok 'API healthy.'
    } else {
        Write-Warn 'Still building or failed — check the "OET API :5198" window; the rest of the stack continues.'
    }
}

# --------------------------------------------------------------------- 3. web
if ($SkipWeb) {
    Write-Step 'Skipping web (-SkipWeb)'
} else {
    Write-Step "Web on $WebUrl"
    if (Get-NetTCPConnection -State Listen -LocalPort $WebPort -ErrorAction SilentlyContinue) {
        Write-Ok 'Already listening; reusing it.'
    } else {
        # .env.development.local pins API_PROXY_TARGET_URL to a dead Podman port -> override here.
        Start-ServiceWindow -Title 'OET web :3000' -Body 'pnpm run dev' -Environment @{
            API_PROXY_TARGET_URL = $ApiUrl
            PUBLIC_API_BASE_URL  = $ApiUrl
        }
        if (Wait-ForHttp -Url $WebUrl -TimeoutSeconds 180 -Label 'web dev server') { Write-Ok 'Web up.' }
    }
}

# ----------------------------------------------------------------- 4. desktop
if ($SkipDesktop) {
    Write-Step 'Skipping desktop (-SkipDesktop)'
} else {
    Write-Step 'Desktop shell (Tauri) -> localhost'
    if (Get-Process oet-desktop -ErrorAction SilentlyContinue) {
        Write-Ok 'Already running; reload its window to pick up localhost.'
    } else {
        Write-Ok 'Launching (first cargo build ~7 min; the launcher exits 0 while the app keeps running).'
        Start-ServiceWindow -Title 'OET desktop (Tauri)' `
            -Body 'node ./scripts/tauri-dev.cjs' `
            -Environment @{ OET_DESKTOP_WEB_URL = $WebUrl }
    }
}

# ------------------------------------------------------------- 5. mobile view
$MobileUrl = "http://localhost:$MobilePort"
if ($SkipMobile) {
    Write-Step 'Skipping mobile view (-SkipMobile)'
} else {
    Write-Step "Mobile view on $MobileUrl"
    if (Get-NetTCPConnection -State Listen -LocalPort $MobilePort -ErrorAction SilentlyContinue) {
        Write-Ok 'Already listening; reusing it.'
    } elseif (-not (Get-Command python -ErrorAction SilentlyContinue)) {
        Write-Warn "'python' not found on PATH; skipping the mobile-view static server."
        Write-Warn "Serve $MobileDir yourself on any port, or just open ${WebUrl} in a phone-sized browser window."
    } else {
        # Static phone-frame page that iframes $WebUrl — Capacitor loads the same app in a
        # webview with no dedicated mobile route/bundle, so this is the closest local stand-in
        # short of a real Android emulator (none is installed on this host).
        Start-ServiceWindow -Title 'OET mobile view :3001' `
            -Body "Set-Location '$MobileDir'; python -m http.server $MobilePort --bind 127.0.0.1"
        if (Wait-ForHttp -Url $MobileUrl -TimeoutSeconds 30 -Label 'mobile view') { Write-Ok 'Mobile view up.' }
    }
}

# ------------------------------------------------------------------- summary
Write-Host ''
Write-Step 'Local stack'
Write-Host "    web      $WebUrl"
Write-Host "    api      $ApiUrl  (-> LIVE prod DB via 127.0.0.1:$TunnelPort)"
Write-Host "    desktop  Tauri window loading $WebUrl"
Write-Host "    mobile   $MobileUrl  (phone-frame iframe of $WebUrl; no AVD image installed on this host)"
Write-Host ''
Write-Warn 'Every write from these apps hits PRODUCTION data, and the API applies pending migrations to prod on boot.'
Write-Host "    Stop everything: pwsh -File scripts/start-local-prod.ps1 -Stop" -ForegroundColor DarkGray
