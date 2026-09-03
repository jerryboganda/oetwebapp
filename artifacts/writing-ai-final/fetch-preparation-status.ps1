#Requires -Version 5.1
<#
.SYNOPSIS
  Exports the Writing preparation-status audit to CSV for the backfill workflow.

.DESCRIPTION
  Pages GET /v1/admin/writing/tasks/preparation-status and writes
  writing-preparation-status.csv (plus a blocking-code summary to console).
  Fill in API_BASE and a content-read admin bearer token. Read-only.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File fetch-preparation-status.ps1 `
    -ApiBase https://api.oetwithdrhesham.co.uk -Token $env:OET_ADMIN_TOKEN
#>
param(
  [string]$ApiBase = 'https://api.oetwithdrhesham.co.uk',
  [Parameter(Mandatory = $true)][string]$Token,
  [string]$Status = 'published',
  [string]$OutCsv = 'writing-preparation-status.csv'
)

$headers = @{ Authorization = "Bearer $Token" }
$page = 1
$rows = @()
do {
  $uri = "$ApiBase/v1/admin/writing/tasks/preparation-status?status=$Status&page=$page&pageSize=100"
  $resp = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
  foreach ($i in $resp.items) {
    $rows += [pscustomobject]@{
      scenarioId            = $i.scenarioId
      title                 = $i.title
      profession            = $i.profession
      letterType            = $i.letterType
      status                = $i.status
      hasTaskPrompt         = $i.hasTaskPrompt
      caseNoteSentenceCount = $i.caseNoteSentenceCount
      rulebookResolvable    = $i.rulebookResolvable
      modelAnswerStatus     = $i.modelAnswerStatus
      modelAnswerApproved   = $i.modelAnswerApproved
      modelAnswerStale      = $i.modelAnswerStale
      publishReady          = $i.publishReady
      blockingCodes         = ($i.blockingCodes -join ';')
    }
  }
  $page++
} while ($resp.items.Count -eq 100)

$rows | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding UTF8
$notReady = @($rows | Where-Object { $_.publishReady -eq $false })
Write-Output "Total: $($rows.Count); not ready: $($notReady.Count); CSV: $OutCsv"
$notReady | Group-Object -Property blockingCodes | Sort-Object Count -Descending |
  Select-Object Count, Name | Format-Table -AutoSize | Out-String | Write-Output
