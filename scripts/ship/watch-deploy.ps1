# Watch Build & Deploy for a SHA, dump failed logs, verify live health.
# Does not invent code fixes. Non-zero exit means the agent must fix and push again
# WITHOUT waiting for the owner.
# Flip public, run on GitHub-hosted Actions, flip back private once verified live
# (owner directive 2026-09-22, HARD ENFORCED — see AGENTS.md "GitHub Actions on a
# public-when-working repo"). -SkipPublic/-SkipPrivateFlip exist for a caller that
# is already managing visibility itself (e.g. coordinating with another agent
# session sharing the same public window) — do not pass them by default.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/ship/watch-deploy.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/ship/watch-deploy.ps1 -Sha <fullsha>
[CmdletBinding()]
param(
    [string]$Repo = 'jerryboganda/oetwebapp',
    [string]$Sha = '',
    [string]$PushStartedAt = '',
    [string]$PushBaseSha = '',
    # $Workflow is the display name used for the EXACT-match filter; $WorkflowFile
    # is what `gh run list --workflow` is given, because a long-deleted workflow
    # is still registered under the name "Deploy Production" (id 254806378) and
    # `gh` refuses a name that resolves to two workflows. The file path is unique.
    [string]$Workflow = 'Deploy production',
    [string]$WorkflowFile = 'production-deploy.yml',
    [int]$WaitForRunSeconds = 180,
    [int]$PollSeconds = 10,
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

# Does $Candidate contain $Ancestor? Asked of GitHub, not of the local clone:
# the live slot can be running a commit this workstation never fetched, and the
# compare API's `.status` is remote truth ('ahead'/'identical' = contains).
# Returns $false on any doubt - an unanswerable question must fail closed.
function Test-ContainsSha {
    param(
        [Parameter(Mandatory = $true)][string]$Ancestor,
        [Parameter(Mandatory = $true)][string]$Candidate
    )
    if ($Candidate -eq $Ancestor) { return $true }
    $comparison = (Invoke-GhJson @('api', "repos/$Repo/compare/$Ancestor...$Candidate")) | ConvertFrom-Json
    return (($comparison.status -eq 'ahead' -or $comparison.status -eq 'identical') -and $comparison.merge_base_commit.sha -eq $Ancestor)
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

# Since 2026-10-03 the deploy is a `workflow_run`-triggered workflow: the
# ROLLOUT run only exists once `Build images` for this SHA has finished
# (~5-10 min). So wait for the whole chain, not for a run that cannot exist yet:
#   - no 'Build images' run at all        -> nothing to deploy (exit 0)
#   - build queued / in progress          -> keep waiting (print the status)
#   - build failed                        -> dump ITS logs and fail (exit 1)
#   - build green, rollout not created yet -> keep waiting
$waitDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$noBuildGrace = [DateTime]::UtcNow.AddSeconds($WaitForRunSeconds)
$runId = $null
$runUrl = ''
$stoodDown = New-Object 'System.Collections.Generic.HashSet[string]'
$ancestry = @{}
$missingBuildProof = $null
while (-not $runId) {
    if ([DateTime]::UtcNow -ge $waitDeadline) {
        Write-Output 'SHIP-WATCH_TIMEOUT'
        Write-Output 'NEXT: dump failed/in-progress logs, fix if this SHA is failing. Do not stop at deploy-initiated.'
        exit 3
    }

    # workflow_run.headSha can be the newer default-branch checkout, not the
    # version being promoted. The run title and actual promotion prove identity.
    try {
        $recentRaw = Invoke-GhJson @(
            'run', 'list',
            '--repo', $Repo,
            '--workflow', $WorkflowFile,
            '--limit', '10',
            '--json', 'databaseId,headSha,displayTitle,status,conclusion,url,workflowName'
        )
        if ($recentRaw -and $recentRaw -ne '[]') {
            $recent = @($recentRaw | ConvertFrom-Json) | Where-Object { $_.workflowName -eq $Workflow }
            $failedCandidate = $null
            foreach ($candidate in $recent) {
                $id = [string]$candidate.databaseId
                if ($stoodDown.Contains($id)) { continue }
                $titleSha = [regex]::Match([string]$candidate.displayTitle, '\b([0-9a-f]{40})$')
                $candidateSha = if ($titleSha.Success) { $titleSha.Groups[1].Value } else { [string]$candidate.headSha }
                if (-not $ancestry.ContainsKey($candidateSha)) {
                    $ancestry[$candidateSha] = Test-ContainsSha -Ancestor $Sha -Candidate $candidateSha
                }
                if (-not $ancestry[$candidateSha]) { continue }
                if ($candidate.status -ne 'completed') {
                    Write-Output "SHIP-WATCH_STATUS run=$id status=$($candidate.status)"
                    continue
                }
                if ($candidate.conclusion -ne 'success') {
                    if (-not $failedCandidate -and ($candidateSha -eq $Sha -or $stoodDown.Count -gt 0)) {
                        $failedCandidate = $candidate
                    }
                    continue
                }
                if ($candidate.conclusion -eq 'success') {
                    $inventory = (Invoke-GhJson @('api', "repos/$Repo/actions/runs/$id/artifacts?per_page=100")) | ConvertFrom-Json
                    $promoted = @($inventory.artifacts | Where-Object { $_.name -eq 'promotion-proof' -and -not $_.expired }).Count -gt 0
                    if (-not $promoted) {
                        $details = (Invoke-GhJson @('run', 'view', $id, '--repo', $Repo, '--json', 'jobs')) | ConvertFrom-Json
                        $rolloutJob = @($details.jobs | Where-Object { $_.name -eq 'Roll out to the VPS' -and $_.conclusion -eq 'success' })
                        if ($rolloutJob.Count -eq 1) {
                            $modern = @($rolloutJob[0].steps | Where-Object { $_.name -eq 'Immediate successful-descendant check' }).Count -gt 0
                            $legacyRollout = @($rolloutJob[0].steps | Where-Object { $_.name -eq 'Deploy to VPS over SSH (pull + blue/green redeploy)' -and $_.conclusion -eq 'success' }).Count -gt 0
                            $promoted = -not $modern -and $legacyRollout
                        }
                    }
                    if (-not $promoted) {
                        [void]$stoodDown.Add($id)
                        Write-Output "SHIP-WATCH_STOOD_DOWN run=$id - following the actual promoting descendant"
                        continue
                    }
                }
                $runId = $id
                $runUrl = [string]$candidate.url
                if ($candidateSha -ne $Sha) { Write-Output "SHIP-WATCH_SUPERSEDED_BY $candidateSha run $runId" }
                break
            }
            if (-not $runId -and $failedCandidate) {
                $runId = [string]$failedCandidate.databaseId
                $runUrl = [string]$failedCandidate.url
            }
            if ($runId) { break }
        }
    } catch {
        Write-Output "Actions promotion check failed: $($_.Exception.Message)"
    }

    # 3. Follow the build that must exist before any rollout can.
    try {
        $buildRaw = Invoke-GhJson @(
            'run', 'list',
            '--repo', $Repo,
            '--workflow', 'build-images.yml',
            '--commit', $Sha,
            '--branch', 'main',
            '--limit', '20',
            '--json', 'databaseId,status,conclusion,url,displayTitle'
        )
        $builds = @(foreach ($candidateBuild in ($buildRaw | ConvertFrom-Json)) {
            if ($candidateBuild.displayTitle -notlike 'Benchmark build *') { $candidateBuild }
        })
        if ($builds.Count -eq 0) {
            if ($null -eq $missingBuildProof -and $PushBaseSha) {
                $oldRepo = $env:GITHUB_REPOSITORY
                $oldSha = $env:RELEASE_SHA
                $oldBase = $env:PUSH_BASE_SHA
                try {
                    $env:GITHUB_REPOSITORY = $Repo
                    $env:RELEASE_SHA = $Sha
                    $env:PUSH_BASE_SHA = $PushBaseSha
                    $helper = Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'deploy') 'release-manifest.mjs'
                    $proof = & node $helper prove-no-build 2>&1 | Out-String
                    $missingBuildProof = @{ ExitCode = $LASTEXITCODE; Detail = $proof.Trim() }
                } finally {
                    $env:GITHUB_REPOSITORY = $oldRepo
                    $env:RELEASE_SHA = $oldSha
                    $env:PUSH_BASE_SHA = $oldBase
                }
                if ($missingBuildProof.ExitCode -eq 0 -and $missingBuildProof.Detail -match "RELEASE_NO_DEPLOYMENT_INPUTS base=$PushBaseSha sha=$Sha") {
                    Write-Output "SHIP-WATCH_NOTHING_TO_DEPLOY $($missingBuildProof.Detail) - bound push inputs prove production is unchanged."
                    exit 0
                }
            }
            if ([DateTime]::UtcNow -ge $noBuildGrace) {
                $detail = if ($missingBuildProof) { $missingBuildProof.Detail } else { 'No exact before-push base; deployment-free inputs cannot be proven.' }
                Write-Output "SHIP-WATCH_MISSING_BUILD no 'Build images' run for $Sha. $detail"
                exit 2
            }
        } else {
            $build = $builds[0]
            $buildStatus = [string]$build.status
            $buildConclusion = [string]$build.conclusion
            if ($buildStatus -ne 'completed') {
                Write-Output "SHIP-WATCH_BUILD_$buildStatus build $($build.databaseId) - waiting for the rollout to be triggered"
            } elseif ($buildConclusion -ne 'success') {
                Write-Output "SHIP-WATCH_BUILD_FAILED the build concluded $buildConclusion - the rollout never starts."
                Write-Output '----- FAILED BUILD LOGS -----'
                & gh run view ([string]$build.databaseId) --repo $Repo --log-failed
                Write-Output '----- END FAILED BUILD LOGS -----'
                Write-Output 'NEXT: fix the build error above, run `pnpm run ship:gate`, commit, `pnpm run ship` again. Do not wait for the owner.'
                exit 1
            } else {
                Write-Output 'SHIP-WATCH_BUILD_OK build green - waiting for the rollout run to appear'
            }
        }
    } catch {
        Write-Output "build-run check failed: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $PollSeconds
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
    Write-Output 'NEXT: keep the repo public, fix the compile/parse error from the logs, run `pnpm run ship:gate`, commit, push origin/main (no force), rerun this watcher. Do not wait for the owner.'
    exit 1
}

Write-Output 'SHIP-WATCH_DEPLOY_OK'

$healthFailed = $false
$publicShas = @()
$publicSlots = @()
$checks = @(
    @{ Name = 'web'; Url = 'https://app.oetwithdrhesham.co.uk/api/health' },
    @{ Name = 'api-ready'; Url = 'https://api.oetwithdrhesham.co.uk/health/ready' },
    @{ Name = 'api-live'; Url = 'https://api.oetwithdrhesham.co.uk/health/live' }
)
foreach ($check in $checks) {
    try {
        $body = & curl.exe -fsS -D - -m 15 $check.Url
        if ($LASTEXITCODE -ne 0) { throw "curl failed with exit $LASTEXITCODE" }
        Write-Output "LIVE $($check.Name): $body"
        $response = @($body) -join "`n"
        $httpStatuses = [regex]::Matches($response, '(?im)^HTTP/[0-9.]+\s+([0-9]{3})\b')
        if ($httpStatuses.Count -eq 0 -or $httpStatuses[$httpStatuses.Count - 1].Groups[1].Value -ne '200') {
            throw 'Public health must return direct HTTP 200.'
        }
        $releaseHeader = [regex]::Match($response, '(?im)^X-Oet-Release:\s*([0-9a-f]{40})\s*$')
        $slotHeader = [regex]::Match($response, '(?im)^X-Oet-Slot:\s*(blue|green)\s*$')
        if (-not $releaseHeader.Success -or -not $slotHeader.Success) { throw 'Serving release/slot headers are missing.' }
        $publicShas += $releaseHeader.Groups[1].Value
        $publicSlots += $slotHeader.Groups[1].Value
    } catch {
        Write-Output "LIVE $($check.Name) FAILED: $($_.Exception.Message)"
        $healthFailed = $true
    }
}

if (-not $SkipVpsSsh) {
    try {
        $remote = @'
set -e
cd /opt/oetwebapp
cat .deploy/live-release.env
slot="$(sed -n 's/^ACTIVE_SLOT=//p' .deploy/live-release.env)"
sha="$(sed -n 's/^RELEASE_SHA=//p' .deploy/live-release.env)"
case "$slot" in blue|green) ;; *) exit 1 ;; esac
for kind in web api; do
  container="oet-$kind-$slot"
  image="$(sed -n "s/^$(printf '%s' "$kind" | tr 'a-z' 'A-Z')_IMAGE=//p" .deploy/live-release.env)"
  actual="$(docker inspect -f '{{.Image}}' "$container")"
  expected="$(docker image inspect -f '{{.Id}}' "$image")"
  alias="$(docker image inspect -f '{{.Id}}' "ghcr.io/jerryboganda/oetwebapp-$kind:$sha")"
  test "$actual" = "$expected"
  test "$actual" = "$alias"
  docker exec "oet-$kind" nginx -T 2>/dev/null | grep -F "$kind-$slot"
  printf 'SERVING_IMAGE_OK=%s\n' "$kind"
done
'@
        $inspect = & ssh -o BatchMode=yes -o ConnectTimeout=12 -o StrictHostKeyChecking=accept-new root@185.252.233.186 $remote
        if ($LASTEXITCODE -ne 0) { throw "Serving image proof failed with SSH exit $LASTEXITCODE" }
        Write-Output $inspect
        # Join first. PowerShell -match/-notmatch on a string[] filters the
        # array; leftover green lines would look like a miss even when blue
        # already carries this SHA.
        $inspectText = @($inspect | ForEach-Object { [string]$_ }) -join "`n"

        $liveSha = [regex]::Match($inspectText, '(?m)^RELEASE_SHA=([0-9a-f]{40})$').Groups[1].Value
        $activeSlot = [regex]::Match($inspectText, '(?m)^ACTIVE_SLOT=(blue|green)$').Groups[1].Value
        if (-not $liveSha -or -not $activeSlot -or $inspectText -notmatch 'SERVING_IMAGE_OK=web' -or $inspectText -notmatch 'SERVING_IMAGE_OK=api' `
            -or $publicShas.Count -ne 3 -or @($publicShas | Where-Object { $_ -ne $liveSha }).Count -gt 0 `
            -or @($publicSlots | Where-Object { $_ -ne $activeSlot }).Count -gt 0 `
            -or -not (Test-ContainsSha -Ancestor $Sha -Candidate $liveSha)) {
            throw 'LIVE_SHA_MISMATCH public routing and immutable serving images do not prove this release.'
        }
        if ($liveSha -ne $Sha) { Write-Output "SHIP-WATCH_SUPERSEDED_BY_LIVE $liveSha" }
        Write-Output "LIVE_SHA_OK $Sha via $liveSha (serving slot: $activeSlot)"
    } catch {
        Write-Output "VPS_SERVING_PROOF_FAILED: $($_.Exception.Message)"
        $healthFailed = $true
    }
}

if ($healthFailed) {
    Write-Output 'SHIP-WATCH_LIVE_FAIL'
    Write-Output 'NEXT: keep investigating live health until the new SHA is healthy. Do not wait for the owner to ask.'
    exit 4
}

if ($PushStartedAt) {
    $started = [DateTimeOffset]::Parse($PushStartedAt)
    $verified = [DateTimeOffset]::UtcNow
    $elapsedSeconds = [Math]::Round(($verified - $started).TotalSeconds, 3)
    if ($elapsedSeconds -lt 0) { throw 'Invalid push-to-live timing: the start is in the future.' }
    $targetResult = if ($SkipVpsSsh) { 'UNVERIFIED' } elseif ($elapsedSeconds -le 300) { 'MET' } else { 'MISSED' }
    Write-Output "SHIP-WATCH_PUSH_TO_VERIFIED_LIVE seconds=$elapsedSeconds target_seconds=300 result=$targetResult boundary=before_push_to_verified_live sha=$Sha run=$runId"
}

if (-not $SkipPrivateFlip) {
    # Lease-aware (owner directive 2026-10-03): never flip private while
    # another agent holds a ship lease, or while ANY hosted run is queued or
    # in progress - those runs would be refused on a private repo. The single
    # implementation of that decision lives in the ship CLI.
    $mayFlip = $true
    $shipCli = Join-Path (Split-Path -Parent $PSCommandPath) 'ship.mjs'
    if (Test-Path $shipCli) {
        $verdict = & node $shipCli --may-flip-private 2>&1
        $verdict | ForEach-Object { Write-Output "SHIP-WATCH_$($_)" }
        if ($LASTEXITCODE -ne 0) { $mayFlip = $false }
    }
    if ($mayFlip) {
        Set-RepoVisibility -Visibility private
    } else {
        Write-Output 'SHIP-WATCH_KEEPING_PUBLIC another lease or in-flight run exists'
    }
}

Write-Output 'SHIP-WATCH_DONE'
exit 0
