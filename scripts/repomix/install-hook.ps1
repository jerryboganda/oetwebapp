# Install the repomix post-commit git hook for this clone.
# Idempotent - safe to run multiple times.
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$gitDir = Join-Path $root '.git'
if (-not (Test-Path $gitDir)) {
    Write-Host "Not a git repo at $root - skipping hook install." -ForegroundColor Yellow
    exit 0
}

$hooksDir = Join-Path $gitDir 'hooks'
New-Item -ItemType Directory -Force -Path $hooksDir | Out-Null

$src = Join-Path $PSScriptRoot 'post-commit.sh'
$dst = Join-Path $hooksDir 'post-commit'

Copy-Item -Path $src -Destination $dst -Force

# Make sure it's executable on systems that honor the bit.
$chmod = Get-Command chmod -ErrorAction SilentlyContinue
if ($chmod) {
    & $chmod.Source '+x' $dst 2>$null
}

Write-Host "Installed repomix post-commit hook -> $dst" -ForegroundColor Green
Write-Host "After every git commit, repomix-output.xml will refresh in the background." -ForegroundColor Green
