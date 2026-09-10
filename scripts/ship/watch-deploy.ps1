# Watch Build & Deploy for a SHA, dump failed logs, verify live health.
# Does not invent code fixes. Non-zero exit means the agent must fix and push again
# WITHOUT waiting for the owner. Keep the repo public on failure so the next
# Actions logs still work. Flip private only after this SHA's deploy succeeds.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/ship/watch-deploy.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/ship/watch-deploy.ps1 -Sha <fullsha>
[CmdletBinding()]
param(
    [string]$Repo = 'jerryboganda/oetwebapp',
    [string]$Sha = '',
    [string]$Workflow = 'Build & Deploy (web + API)',
    [int]$WaitForRunSeconds = 180,
    [int]$PollSeconds = 25,
    [int]$TimeoutSeconds = 1800,
    [switch]$SkipPublic,
    [switch]$SkipPrivateFlip,
    [switch]$SkipVpsSsh
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$GhArgs)
    $raw = & gh @GhArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($GhArgs -join ' ') failed: $raw"
    }
    return $raw.Trim()
}

function Set-RepoVisibility {
    param([ValidateSet('public', 'private')][string]$Visibility)
    Write-Output "VISIBILITY -> $Visibility"
    & gh repo edit $Repo --visibility $Visibility --accept-visibility-change-consequences
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set $Repo visibility to $Visibility"
    }
}

if (-not $Sha) {
    $Sha = 'HEAD'
}
$resolved = (& git rev-parse --verify $Sha).Trim()
if (-not $resolved -or $resolved -notmatch '^[0-9a-f]{40}$') {
    throw "Cannot resolve full commit SHA from '$Sha'"
}
$Sha = $resolved

Write-Output "SHIP-WATCH repo=$Repo sha=$Sha workflow=$Workflow"

if (-not $SkipPublic) {
    Set-RepoVisibility -Visibility public
}

$deadline = [DateTime]::UtcNow.AddSeconds($WaitForRunSeconds)
$runId = $null
$runUrl = ''
do {
    try {
        $listRaw = Invoke-GhJson @(
            'run', 'list',
            '--repo', $Repo,
            '--workflow', $Workflow,
            '--commit', $Sha,
            '--json', 'databaseId,status,conclusion,url',
            '--limit', '5'
        )
        if ($listRaw -and $listRaw -ne '[]') {
            $runs = $listRaw | ConvertFrom-Json
            if ($runs -and $runs.Count -gt 0) {
                $runId = [string]$runs[0].databaseId
                $runUrl = [string]$runs[0].url
                break
            }
        }
    } catch {
        Write-Output "waiting for Actions run: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 8
} while ([DateTime]::UtcNow -lt $deadline)

if (-not $runId) {
    Write-Output 'SHIP-WATCH_NO_RUN'
    Write-Output 'NEXT: dump `gh run list`, confirm the repo is public, then retry this watcher. Do not tell the owner the deploy is done.'
    exit 2
}

Write-Output "SHIP-WATCH_RUN $runId $runUrl"

$watchDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$status = 'unknown'
$conclusion = ''
do {
    $viewRaw = Invoke-GhJson @(
        'run', 'view', $runId,
        '--repo', $Repo,
        '--json', 'status,conclusion,url'
    )
    $view = $viewRaw | ConvertFrom-Json
    $status = [string]$view.status
    $conclusion = [string]$view.conclusion
    Write-Output "SHIP-WATCH_STATUS status=$status conclusion=$conclusion"
    if ($status -eq 'completed') { break }
    if ([DateTime]::UtcNow -ge $watchDeadline) {
        Write-Output 'SHIP-WATCH_TIMEOUT'
        Write-Output 'NEXT: dump failed/in-progress logs, fix if the SHA is already failing, do not stop at deploy-initiated.'
        exit 3
    }
    Start-Sleep -Seconds $PollSeconds
} while ($true)

if ($conclusion -ne 'success') {
    Write-Output "SHIP-WATCH_FAILED conclusion=$conclusion url=$runUrl"
    Write-Output '----- FAILED LOGS -----'
    & gh run view $runId --repo $Repo --log-failed
    Write-Output '----- END FAILED LOGS -----'
    Write-Output 'NEXT: keep repo PUBLIC, fix the compile/parse error from the logs, run `pnpm run ship:gate`, commit, push origin/main (no force), rerun this watcher. Do not wait for the owner.'
    exit 1
}

Write-Output 'SHIP-WATCH_DEPLOY_OK'

$healthFailed = $false
$checks = @(
    @{ Name = 'web'; Url = 'https://app.oetwithdrhesham.co.uk/api/health' },
    @{ Name = 'api-ready'; Url = 'https://api.oetwithdrhesham.co.uk/health/ready' },
    @{ Name = 'api-live'; Url = 'https://api.oetwithdrhesham.co.uk/health/live' }
)
foreach ($check in $checks) {
    try {
        $body = & curl.exe -fsS -m 15 $check.Url
        Write-Output "LIVE $($check.Name): $body"
    } catch {
        Write-Output "LIVE $($check.Name) FAILED: $($_.Exception.Message)"
        $healthFailed = $true
    }
}

if (-not $SkipVpsSsh) {
    try {
        $remote = @"
docker inspect oet-web-blue oet-web-green oet-api-blue oet-api-green oet-agent-gateway --format 'NAME={{.Name}} HEALTH={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}} IMAGE={{.Config.Image}}' 2>/dev/null
docker exec oet-api sh -c 'echo ROUTER_ACTIVE_SLOT=`$ACTIVE_SLOT' 2>/dev/null
"@
        $inspect = & ssh -o BatchMode=yes -o ConnectTimeout=12 -o StrictHostKeyChecking=accept-new root@185.252.233.186 $remote
        Write-Output $inspect
        # Join first. PowerShell -match/-notmatch on a string[] filters the
        # array; leftover green lines would look like a miss even when blue
        # already carries this SHA.
        $inspectText = @($inspect | ForEach-Object { [string]$_ }) -join "`n"
        $escaped = [regex]::Escape($Sha)

        # Root-cause fix (Writing Rule Enforcement Addendum Rev5, 10 Sep
        # 2026): checking "does EITHER blue or green carry this SHA" only
        # proves the image was PULLED, not that the router (oet-api/oet-web,
        # which reads $ACTIVE_SLOT to pick learner-api-<slot>/web-<slot> as
        # its proxy_pass target) is actually SENDING PUBLIC TRAFFIC to that
        # slot. Confirmed live 10 Sep 2026: auto-deploy-ghcr.sh reported
        # "AUTO_DEPLOY_DONE: live on green" and this exact check reported
        # LIVE_SHA_OK, while oet-api's baked ACTIVE_SLOT env var was actually
        # "blue" (the router recreate step's `ACTIVE_SLOT="$slot" docker
        # compose ... -f "$COMPOSE_FILE" up ...` resolved the compose
        # file's `${ACTIVE_SLOT:-blue}` to its fallback default) — the OLD
        # commit kept serving all public traffic while this script reported
        # success. Now require the slot the router is ACTUALLY pointed at,
        # per ROUTER_ACTIVE_SLOT above, to carry this SHA — not just any slot.
        if ($inspectText -notmatch 'ROUTER_ACTIVE_SLOT=(blue|green)') {
            Write-Output "LIVE_SHA_MISMATCH could not read the router's active slot"
            $healthFailed = $true
        } else {
            $activeSlot = $Matches[1]
            $hasWeb = $inspectText -match ("NAME=/oet-web-$activeSlot.*" + $escaped)
            $hasApi = $inspectText -match ("NAME=/oet-api-$activeSlot.*" + $escaped)
            if (-not ($hasWeb -and $hasApi)) {
                Write-Output "LIVE_SHA_MISMATCH router is serving slot '$activeSlot', which is not tagged $Sha — traffic is still on the OLD build"
                $healthFailed = $true
            } else {
                Write-Output "LIVE_SHA_OK $Sha (serving slot: $activeSlot)"
            }
        }
    } catch {
        Write-Output "VPS_SSH_SKIPPED: $($_.Exception.Message)"
    }
}

if ($healthFailed) {
    Write-Output 'SHIP-WATCH_LIVE_FAIL'
    Write-Output 'NEXT: keep investigating live health. Do not flip private until the new SHA is healthy. Do not wait for the owner to ask.'
    exit 4
}

if (-not $SkipPrivateFlip) {
    Set-RepoVisibility -Visibility private
}

Write-Output 'SHIP-WATCH_DONE'
exit 0
