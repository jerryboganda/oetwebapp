# ==========================================================================
#  OET Prep Platform — OpenCode / oh-my-opencode activation (PowerShell)
# ==========================================================================
#
#  Dot-source this ONCE per PowerShell session before launching `opencode`:
#
#      Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
#      . .\.opencode\activate.ps1
#      opencode
#
#  Telemetry is disabled so nothing phones home.
# ==========================================================================

$projectRoot = Split-Path -Parent $PSScriptRoot
$omoRoot     = Join-Path $projectRoot '.opencode'

$env:OMO_SEND_ANONYMOUS_TELEMETRY = '0'
$env:OMO_DISABLE_POSTHOG          = '1'

$binA = Join-Path $omoRoot 'node_modules\.bin'
$binB = Join-Path $omoRoot 'node_modules\@code-yeongyu\comment-checker\bin'

# Prepend project binaries. Avoid duplicating if already present.
foreach ($p in @($binA, $binB)) {
    if (-not ($env:PATH -split ';' -contains $p)) {
        $env:PATH = "$p;$($env:PATH)"
    }
}

Write-Host '[omo] activate.ps1: PATH primed.' -ForegroundColor Cyan
Write-Host "[omo]   + $binA" -ForegroundColor DarkGray
Write-Host "[omo]   + $binB" -ForegroundColor DarkGray
Write-Host '[omo] OMO_SEND_ANONYMOUS_TELEMETRY=0  OMO_DISABLE_POSTHOG=1' -ForegroundColor DarkGray
Write-Host '[omo] You can now run: opencode' -ForegroundColor Green
