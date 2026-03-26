param(
    [int]$Port = 5080,
    [int]$StartupTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

$stdoutLog = Join-Path $env:TEMP "oet-api-prod-probe.stdout.log"
$stderrLog = Join-Path $env:TEMP "oet-api-prod-probe.stderr.log"
$storageRoot = Join-Path $env:TEMP "oet-learner-prod-probe-storage"
$appDllPath = "backend/src/OetLearner.Api/bin/Release/net10.0/OetLearner.Api.dll"
$healthUrl = "http://127.0.0.1:$Port/health/ready"

Remove-Item $stdoutLog, $stderrLog -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $storageRoot -Force | Out-Null

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ConnectionStrings__DefaultConnection = "InMemory:oet-learner-prod-probe"
$env:Auth__Authority = ""
$env:Auth__Audience = "oet-learner-api"
$env:Auth__Issuer = "https://auth.example.local"
$env:Auth__SigningKey = "0123456789abcdef0123456789abcdef"
$env:Auth__RequireHttpsMetadata = "false"
$env:Platform__PublicApiBaseUrl = "http://127.0.0.1:$Port"
$env:Billing__CheckoutBaseUrl = "http://127.0.0.1:3000/billing/checkout"
$env:Cors__AllowedOriginsCsv = "http://127.0.0.1:3000"
$env:Proxy__EnforceHttps = "false"
$env:Storage__LocalRootPath = $storageRoot
$env:Bootstrap__AutoMigrate = "false"
$env:Bootstrap__SeedDemoData = "false"
$env:Features__EnableSwagger = "false"

$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList $appDllPath `
    -WorkingDirectory (Get-Location) `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog

try {
    for ($attempt = 0; $attempt -lt $StartupTimeoutSeconds; $attempt++) {
        Start-Sleep -Seconds 1

        if ($process.HasExited) {
            break
        }

        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Output "READY_STATUS=200"
                Write-Output "READY_BODY=$($response.Content)"
                exit 0
            }
        }
        catch {
        }
    }

    if ($process.HasExited) {
        Write-Output "Process exited with code $($process.ExitCode)."
    }

    Write-Output "STDOUT:"
    if (Test-Path $stdoutLog) {
        Get-Content $stdoutLog
    }

    Write-Output "STDERR:"
    if (Test-Path $stderrLog) {
        Get-Content $stderrLog
    }

    exit 1
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
