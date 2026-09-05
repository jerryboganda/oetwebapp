param(
    [string]$ImageListJson,
    [string]$OutputJson
)

Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime] | Out-Null

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1
} | Select-Object -First 1

function AwaitTask($WinRtOp, $Type) {
    $netTask = $asTask.MakeGenericMethod($Type).Invoke($null, @($WinRtOp))
    $netTask.Wait()
    return $netTask.Result
}

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if (-not $engine) {
    Write-Error "No OCR engine available"
    exit 1
}

$images = Get-Content -Raw $ImageListJson | ConvertFrom-Json
$results = @{}

foreach ($imgPath in $images) {
    if (-not (Test-Path $imgPath)) {
        $results[$imgPath] = ""
        continue
    }
    try {
        $fullPath = (Resolve-Path $imgPath).Path
        $file = AwaitTask ([Windows.Storage.StorageFile]::GetFileFromPathAsync($fullPath)) ([Windows.Storage.StorageFile])
        $stream = AwaitTask ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
        $decoder = AwaitTask ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = AwaitTask ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $ocr = AwaitTask ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $results[$imgPath] = $ocr.Text
    } catch {
        $results[$imgPath] = ""
    }
}

$results | ConvertTo-Json -Depth 2 | Set-Content -Path $OutputJson -Encoding UTF8
Write-Host "Processed $($images.Count) images."
