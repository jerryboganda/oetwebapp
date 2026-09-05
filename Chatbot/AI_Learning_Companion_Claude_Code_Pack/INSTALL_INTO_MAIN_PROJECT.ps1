[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)]
    [string]$TargetRepo
)

$ErrorActionPreference = 'Stop'
$PackRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Target = (Resolve-Path $TargetRepo).Path

if (-not (Test-Path $Target -PathType Container)) {
    throw "Target repository does not exist: $Target"
}

Write-Host "AI Learning Companion pack: $PackRoot"
Write-Host "Target repository: $Target"

function Copy-PackItem {
    param([string]$RelativePath, [string]$DestinationRelativePath = $RelativePath)
    $src = Join-Path $PackRoot $RelativePath
    $dst = Join-Path $Target $DestinationRelativePath
    if (-not (Test-Path $src)) { throw "Missing pack item: $src" }
    $parent = Split-Path -Parent $dst
    if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    if ($PSCmdlet.ShouldProcess($dst, "Copy $RelativePath")) {
        Copy-Item -Path $src -Destination $dst -Recurse -Force
    }
}

# Project-specific documentation and traceability.
Copy-PackItem 'docs/ai-learning-companion'
Copy-PackItem 'traceability'
Copy-PackItem '.claude/commands'
Copy-PackItem 'AI_LEARNING_COMPANION_CLAUDE_CODE_MASTER_PLAN.markdown.md'
Copy-PackItem 'START_HERE.md' 'AI_COMPANION_START_HERE.md'
Copy-PackItem 'source/Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf' 'docs/ai-learning-companion/source/Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf'

# Preserve any existing project-level CLAUDE.md instead of overwriting it.
$existingClaude = Join-Path $Target 'CLAUDE.md'
if (Test-Path $existingClaude) {
    Copy-PackItem 'CLAUDE.md' 'CLAUDE_AI_COMPANION_ADDENDUM.md'
    Write-Warning 'Existing CLAUDE.md preserved. Merge CLAUDE_AI_COMPANION_ADDENDUM.md into it without weakening existing repository instructions.'
} else {
    Copy-PackItem 'CLAUDE.md'
}

# Add validator under a unique project path; do not overwrite unrelated scripts.
$validatorDir = Join-Path $Target 'scripts/ai-learning-companion'
if (-not (Test-Path $validatorDir)) { New-Item -ItemType Directory -Force -Path $validatorDir | Out-Null }
if ($PSCmdlet.ShouldProcess($validatorDir, 'Install traceability validator')) {
    Copy-Item (Join-Path $PackRoot 'scripts/validate_traceability.py') (Join-Path $validatorDir 'validate_traceability.py') -Force
}

Write-Host ''
Write-Host 'Installation copy completed.'
Write-Host 'Next: open Claude Code in the target repo and run/read .claude/commands/ai-companion-audit.md first.'
Write-Host 'Do not start implementation before the repository gap analysis is complete.'
