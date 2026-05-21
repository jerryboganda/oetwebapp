#!/usr/bin/env pwsh
# Speaking module end-to-end smoke (PowerShell).
$ErrorActionPreference = "Stop"

$api      = $env:OET_API_URL              ; if (-not $api)      { $api = "http://localhost:5199" }
$email    = $env:OET_TEST_LEARNER_EMAIL   ; if (-not $email)    { $email = "e2e-learner@example.com" }
$password = $env:OET_TEST_LEARNER_PASSWORD; if (-not $password) { $password = "please-change-me" }

Write-Host "[smoke] sign-in"
$signin = Invoke-RestMethod -Method Post -Uri "$api/v1/auth/sign-in" `
    -ContentType "application/json" `
    -Body (@{ email = $email; password = $password; rememberMe = $true } | ConvertTo-Json -Compress)
$headers = @{ Authorization = "Bearer $($signin.accessToken)" }

Write-Host "[smoke] pick a published role-play card"
$cards = Invoke-RestMethod -Headers $headers -Uri "$api/v1/speaking/role-play-cards"
if (-not $cards -or $cards.Count -eq 0) {
    Write-Error "no published cards for this learner's profession — run seed-speaking-dev first."
}
$cardId = $cards[0].id

Write-Host "[smoke] create session for card=$cardId"
$session = Invoke-RestMethod -Method Post -Uri "$api/v1/speaking/sessions" `
    -Headers $headers -ContentType "application/json" `
    -Body (@{ rolePlayCardId = $cardId; mode = "AiSelfPractice" } | ConvertTo-Json -Compress)
$sid = $session.session.id

Write-Host "[smoke] warm-up start + finish"
Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/start-warmup"  | Out-Null
Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/finish-warmup" | Out-Null

Write-Host "[smoke] start role-play, end immediately"
Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/start-roleplay" | Out-Null
Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/end"            | Out-Null

Write-Host "[smoke] trigger AI assessment"
Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/ai-assess" | Out-Null

Write-Host "[smoke] poll for assessment..."
for ($i = 0; $i -lt 30; $i++) {
    $assess = Invoke-RestMethod -Headers $headers -Uri "$api/v1/speaking/sessions/$sid/ai-assessment"
    if ($assess.readinessBand) {
        Write-Host "[smoke] PASS — readiness band: $($assess.readinessBand)"
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Error "assessment did not land within 60s"
