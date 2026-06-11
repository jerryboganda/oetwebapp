$ErrorActionPreference = 'SilentlyContinue'

foreach ($port in @(3000, 5198)) {
  Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
}

Write-Host 'Stopped OET frontend and backend. PostgreSQL and project storage remain running/preserved.' -ForegroundColor Green
Start-Sleep -Seconds 5

