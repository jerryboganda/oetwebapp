<#
.SYNOPSIS
  Read-only QA probe: tests every AI model exposed by the UBAG provider
  through the platform's own admin "test model" endpoint.

.DESCRIPTION
  Uses POST /v1/admin/ai/providers/{code}/test-model for each model returned by
  GET /v1/admin/ai/providers/{code}/models. Writes results to a JSON + CSV file
  under the run directory and prints a summary table.

  This makes NO configuration changes: it only exercises provider connections
  and records the persisted test status, exactly like the admin UI's
  "Test model" button.

.NOTES
  Requires an admin token. Pass it via -AccessToken or the OET_ADMIN_TOKEN
  environment variable. Never commit a token.
#>
[CmdletBinding()]
param(
  [string] $ApiBase = 'https://api.oetwithdrhesham.co.uk',
  [string] $ProviderCode = 'ubag',
  [string] $AccessToken = $env:OET_ADMIN_TOKEN,
  [int]    $DelayMs = 250,
  [string] $OutDir = (Join-Path (Get-Location) '.qa-artifacts/ubag-model-probe')
)

$ErrorActionPreference = 'Continue'

if ([string]::IsNullOrWhiteSpace($AccessToken)) {
  throw "An admin access token is required. Pass -AccessToken or set OET_ADMIN_TOKEN."
}

$headers = @{ Authorization = "Bearer $AccessToken" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Host "== Discover models for '$ProviderCode' ==" -ForegroundColor Cyan
$discovered = Invoke-RestMethod -Uri "$ApiBase/v1/admin/ai/providers/$ProviderCode/models" -Headers $headers -TimeoutSec 120
$models = @($discovered.models)
Write-Host "Discovered $($models.Count) models." -ForegroundColor Cyan

$results = New-Object System.Collections.Generic.List[object]
$i = 0

foreach ($model in $models) {
  $i++
  $pct = [int](($i / $models.Count) * 100)
  Write-Progress -Activity "Testing $ProviderCode models" -Status "$i / $($models.Count): $model" -PercentComplete $pct

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $row = [ordered]@{
    model       = $model
    status      = 'error'
    latencyMs   = $null
    wallMs      = $null
    errorMessage= $null
    steps       = @()
  }

  try {
    $body = @{ model = $model } | ConvertTo-Json
    $resp = Invoke-RestMethod -Uri "$ApiBase/v1/admin/ai/providers/$ProviderCode/test-model" `
              -Method Post -Headers $headers -ContentType 'application/json' `
              -Body $body -TimeoutSec 300
    $row.status       = $resp.status
    $row.latencyMs    = $resp.latencyMs
    $row.errorMessage = $resp.errorMessage
    $row.steps        = @($resp.steps)
  }
  catch {
    $row.errorMessage = $_.Exception.Message
    if ($_.ErrorDetails) { $row.errorMessage = $_.ErrorDetails.Message }
  }
  $sw.Stop()
  $row.wallMs = [int]$sw.ElapsedMilliseconds

  $colour = if ($row.status -eq 'ok') { 'Green' } else { 'Red' }
  $line = "{0,3}/{1}  {2,-42} {3,-8} {4,8}ms  {5}" -f $i, $models.Count, $model, $row.status, $row.wallMs, ($row.errorMessage ?? '')
  Write-Host $line -ForegroundColor $colour

  $results.Add([pscustomobject]$row)
  if ($DelayMs -gt 0) { Start-Sleep -Milliseconds $DelayMs }
}

Write-Progress -Activity "Testing $ProviderCode models" -Completed

$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonOut = Join-Path $OutDir "ubag-model-probe-$stamp.json"
$csvOut  = Join-Path $OutDir "ubag-model-probe-$stamp.csv"

$summary = [pscustomobject]@{
  providerCode = $ProviderCode
  apiBase      = $ApiBase
  probedAt     = (Get-Date).ToString('o')
  total        = $results.Count
  ok           = @($results | Where-Object { $_.status -eq 'ok' }).Count
  failed       = @($results | Where-Object { $_.status -ne 'ok' }).Count
  results      = $results
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonOut -Encoding UTF8
$results | Select-Object model, status, latencyMs, wallMs, errorMessage |
  Export-Csv -Path $csvOut -NoTypeInformation -Encoding UTF8

Write-Host ''
Write-Host "== SUMMARY ==" -ForegroundColor Cyan
Write-Host "  total : $($summary.total)"
Write-Host "  ok    : $($summary.ok)"    -ForegroundColor Green
Write-Host "  failed: $($summary.failed)" -ForegroundColor Red
Write-Host "  json  : $jsonOut"
Write-Host "  csv   : $csvOut"
