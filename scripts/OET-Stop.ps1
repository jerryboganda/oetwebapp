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

# Stop PostgreSQL (installed as the Windows service 'postgresql-17' on this machine)
$pgService = 'postgresql-17'
$svc = Get-Service -Name $pgService -ErrorAction SilentlyContinue
if ($null -ne $svc) {
    if ($svc.Status -eq 'Running') {
        Write-Host "  Stopping PostgreSQL service '$pgService'..." -ForegroundColor Cyan
        Stop-Service -Name $pgService -Force -ErrorAction SilentlyContinue
        Write-Host "  PostgreSQL stopped." -ForegroundColor Green
    } else {
        Write-Host "  PostgreSQL service already stopped." -ForegroundColor DarkGray
    }
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
