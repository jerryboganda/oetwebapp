param(
  [string]$Model = "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
  [string]$Prompt = "Reply with exactly: WORKERS_AI_OK",
  [int]$MaxTokens = 32
)

$ErrorActionPreference = "Stop"

$accountId = $env:CLOUDFLARE_ACCOUNT_ID
$token = $env:CLOUDFLARE_API_KEY

if ([string]::IsNullOrWhiteSpace($accountId)) {
  throw 'CLOUDFLARE_ACCOUNT_ID is missing. Run: setx CLOUDFLARE_ACCOUNT_ID "68970844047393a63eb6338799ff40f8"'
}

if ([string]::IsNullOrWhiteSpace($token)) {
  throw 'CLOUDFLARE_API_KEY is missing. Create a scoped Workers AI API token, then run setx CLOUDFLARE_API_KEY "<token>" in your own terminal. Do not paste it into chat.'
}

$url = "https://api.cloudflare.com/client/v4/accounts/$accountId/ai/run/$Model"

$body = @{
  messages = @(
    @{ role = "user"; content = $Prompt }
  )
  max_tokens = $MaxTokens
} | ConvertTo-Json -Depth 6

$headers = @{
  "Authorization" = "Bearer $token"
  "Content-Type" = "application/json"
}

try {
  $response = Invoke-RestMethod -Method POST -Uri $url -Headers $headers -Body $body
  $content = $response.result.response
  Write-Host "Cloudflare Workers AI OK" -ForegroundColor Green
  Write-Host "Model: $Model"
  Write-Host "Response: $content"
}
catch {
  Write-Host "Cloudflare Workers AI smoke test failed" -ForegroundColor Red
  Write-Host "Model: $Model"
  Write-Host "Error: $($_.Exception.Message)"
  if ($_.ErrorDetails.Message) {
    Write-Host "Details: $($_.ErrorDetails.Message)"
  }
  exit 1
}
