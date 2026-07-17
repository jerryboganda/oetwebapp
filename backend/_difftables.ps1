$ErrorActionPreference = 'Stop'
$root = 'C:\Users\Administrator\Desktop\New OET Web App'
$migDir = Join-Path $root 'backend\src\OetWithDrHesham.Api\Data\Migrations'
$snapshot = Join-Path $migDir 'LearnerDbContextModelSnapshot.cs'

$files = Get-ChildItem $migDir -Filter *.cs | Where-Object {
    $_.Name -notlike '*.Designer.cs' -and $_.Name -ne 'LearnerDbContextModelSnapshot.cs'
}

$created = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    $text = Get-Content -Raw $f.FullName
    foreach ($m in [regex]::Matches($text, 'CreateTable\(\s*name:\s*"([^"]+)"')) {
        $created.Add($m.Groups[1].Value)
    }
}
$createdUnique = $created | Sort-Object -Unique

$snapText = Get-Content -Raw $snapshot
$wanted = [regex]::Matches($snapText, 'b\.ToTable\("([^"]+)"\)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

"Created table count: $($createdUnique.Count)"
"Wanted (snapshot) table count: $($wanted.Count)"
"--- In snapshot but NEVER created by any migration ---"
$wanted | Where-Object { $createdUnique -notcontains $_ }
