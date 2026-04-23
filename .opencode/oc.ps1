# ==========================================================================
#  oc.ps1 — ZERO-MANUAL-STEP OpenCode launcher for OET Prep Platform
# ==========================================================================
#
#  Usage:
#    .\.opencode\oc.ps1              # launch OpenCode TUI
#    .\.opencode\oc.ps1 run "fix"    # any opencode subcommand just works
#
#  What this does:
#    1. Disables oh-my-opencode telemetry.
#    2. Prepends project-local binaries to PATH (ast-grep, comment-checker).
#    3. cd's to the project root.
#    4. Launches `opencode`, forwarding every argument.
#
#  Why:
#    Removes the manual `. .\.opencode\activate.ps1` step that was easy
#    to forget and silently degraded the agent environment.
# ==========================================================================

param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$OpenCodeArgs
)

$ErrorActionPreference = 'Stop'

# --- Resolve project root (parent of this script's dir) ---
$scriptDir   = Split-Path -Parent $PSCommandPath
$projectRoot = Split-Path -Parent $scriptDir

# --- 1. Telemetry off ---
$env:OMO_SEND_ANONYMOUS_TELEMETRY = '0'
$env:OMO_DISABLE_POSTHOG          = '1'

# --- 2. Prepend project-local tool binaries ---
$binA = Join-Path $projectRoot '.opencode\node_modules\.bin'
$binB = Join-Path $projectRoot '.opencode\node_modules\@code-yeongyu\comment-checker\bin'
foreach ($p in @($binA, $binB)) {
    if (Test-Path $p) {
        if (-not (($env:PATH -split ';') -contains $p)) {
            $env:PATH = "$p;$($env:PATH)"
        }
    }
}

# --- 3. Go home ---
Set-Location $projectRoot

# --- 4. Launch ---
Write-Host "[oc] PATH primed. telemetry=off. cwd=$projectRoot" -ForegroundColor Cyan
if ($OpenCodeArgs) {
    & opencode @OpenCodeArgs
} else {
    & opencode
}
exit $LASTEXITCODE
