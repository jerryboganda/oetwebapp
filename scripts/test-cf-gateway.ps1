param(
  [string]$Model = "anthropic/claude-opus-4.7",
  [string]$Prompt = "Reply with exactly: GATEWAY_OK",
  [int]$MaxTokens = 32,
  [switch]$NoAuth
)

$ErrorActionPreference = "Stop"

$accountId = $env:CLOUDFLARE_ACCOUNT_ID
$gatewayId = $env:CLOUDFLARE_GATEWAY_ID
$token = $env:CF_AIG_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
  $token = $env:CLOUDFLARE_API_TOKEN
}

if ([string]::IsNullOrWhiteSpace($accountId)) {
  throw 'CLOUDFLARE_ACCOUNT_ID is missing. Run: setx CLOUDFLARE_ACCOUNT_ID "68970844047393a63eb6338799ff40f8"'
}

if ([string]::IsNullOrWhiteSpace($gatewayId)) {
  throw 'CLOUDFLARE_GATEWAY_ID is missing. Run: setx CLOUDFLARE_GATEWAY_ID "opencode"'
}

if (-not $NoAuth -and [string]::IsNullOrWhiteSpace($token)) {
  throw 'CF_AIG_TOKEN/CLOUDFLARE_API_TOKEN is missing. Create a new AI Gateway auth token, then run setx CLOUDFLARE_API_TOKEN "<token>" in your own terminal. Do not paste it into chat.'
}

$url = "https://gateway.ai.cloudflare.com/v1/$accountId/$gatewayId/compat/chat/completions"

$body = @{
  model = $Model
  messages = @(
    @{ role = "user"; content = $Prompt }
  )
  max_tokens = $MaxTokens
  temperature = 0.2
} | ConvertTo-Json -Depth 6

$headers = @{
  "Content-Type" = "application/json"
}

if (-not $NoAuth) {
  $headers["Authorization"] = "Bearer $token"
}

try {
  $response = Invoke-RestMethod -Method POST -Uri $url -Headers $headers -Body $body
  $content = $response.choices[0].message.content
  Write-Host "Cloudflare AI Gateway OK" -ForegroundColor Green
  Write-Host "Model: $Model"
  Write-Host "Gateway: $gatewayId"
  Write-Host "Auth header sent: $(-not $NoAuth)"
  Write-Host "Response: $content"
}
catch {
  Write-Host "Cloudflare AI Gateway smoke test failed" -ForegroundColor Red
  Write-Host "Model: $Model"
  Write-Host "Gateway: $gatewayId"
  Write-Host "Auth header sent: $(-not $NoAuth)"
  Write-Host "Error: $($_.Exception.Message)"
  if ($_.ErrorDetails.Message) {
    Write-Host "Details: $($_.ErrorDetails.Message)"
  }
  if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) {
    Write-Host "Hint: 401 means the request reached the gateway, but the AI Gateway token is invalid, expired, revoked, or not for this gateway. Rotate it from AI Gateway > opencode > Create token, then set CLOUDFLARE_API_TOKEN and CF_AIG_TOKEN to the new value without angle brackets."
  }
  exit 1
}
