# OET-Stop.ps1 - Stop all OET Prep Platform local services

$ErrorActionPreference = 'SilentlyContinue'

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "   Stopping OET Prep Platform...       " -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

# Find and terminate processes on port 3000 (frontend)
$conns3000 = Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue
foreach ($c in $conns3000) {
    $targetPid = $c.OwningProcess
    Write-Host "  Stopping frontend (PID $targetPid)..." -ForegroundColor Cyan
    [System.Diagnostics.Process]::GetProcessById($targetPid).Kill()
}

# Find and terminate processes on port 5198 (backend)
$conns5198 = Get-NetTCPConnection -LocalPort 5198 -State Listen -ErrorAction SilentlyContinue
foreach ($c in $conns5198) {
    $targetPid = $c.OwningProcess
    Write-Host "  Stopping backend (PID $targetPid)..." -ForegroundColor Cyan
    [System.Diagnostics.Process]::GetProcessById($targetPid).Kill()
}

# Stop PostgreSQL gracefully using pg_ctl
$pgBin = "$env:USERPROFILE\scoop\apps\postgresql\current\bin"
$pgData = "$env:USERPROFILE\scoop\persist\postgresql\data"
$pgCtl = Join-Path $pgBin 'pg_ctl.exe'

if (Test-Path $pgCtl) {
    Write-Host "  Stopping PostgreSQL..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $pgCtl -ArgumentList "stop -D `"$pgData`" -m fast" -WindowStyle Hidden -PassThru
    $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    if (-not $proc.HasExited) {
        Write-Host "  PostgreSQL stop timed out, killing by port..." -ForegroundColor Yellow
        $conns5432 = Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue
        foreach ($c in $conns5432) {
            [System.Diagnostics.Process]::GetProcessById($c.OwningProcess).Kill()
        }
    }
    Write-Host "  PostgreSQL stopped." -ForegroundColor Green
} else {
    $conns5432 = Get-NetTCPConnection -LocalPort 5432 -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns5432) {
        Write-Host "  Stopping process on port 5432 (PID $($c.OwningProcess))..." -ForegroundColor Cyan
        [System.Diagnostics.Process]::GetProcessById($c.OwningProcess).Kill()
    }
}

Write-Host ""
Write-Host "  All OET services stopped." -ForegroundColor Green
Write-Host ""
Write-Host "This window will close in 5 seconds..." -ForegroundColor DarkGray
Start-Sleep -Seconds 5
