[CmdletBinding()]
param(
    [switch]$DryRun,
    [string[]]$Agents = @("universal", "codex", "claude-code")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$autoskillsEntry = Join-Path $repoRoot ".tools\autoskills\packages\autoskills\index.mjs"

if (-not (Test-Path -LiteralPath $autoskillsEntry)) {
    throw "autoskills entrypoint not found: $autoskillsEntry"
}

$arguments = @($autoskillsEntry, "-y", "-a") + $Agents

if ($DryRun) {
    $arguments += "--dry-run"
}

Write-Host "Running autoskills from $repoRoot"
Write-Host "Agents: $($Agents -join ', ')"
if ($DryRun) {
    Write-Host "Mode: dry-run"
}

Push-Location $repoRoot
try {
    & node @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "autoskills exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
