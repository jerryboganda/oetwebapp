# Add _sanitizeTtsText helper and wire it into _ttsChunkText.
# Re-runnable: detects existing function and skips if already patched.

$ErrorActionPreference = 'Stop'
$path = 'scripts/admin/_lib.mjs'
$text = [System.IO.File]::ReadAllText($path)

if ($text -match 'function\s+_sanitizeTtsText\s*\(') {
  Write-Host "Already patched. Skipping."
  exit 0
}

$marker = "function _ttsChunkText(text) {`n  const t = String(text || '').replace(/\s+/g, ' ').trim();"
$helper = @'
function _sanitizeTtsText(text) {
  let s = String(text || '');
  // Replace curly quotes/apostrophes with ASCII equivalents.
  s = s.replace(/[\u2018\u2019\u201A\u201B]/g, "'");
  s = s.replace(/[\u201C\u201D\u201E\u201F]/g, '"');
  // Em/en dashes, hyphen variants → ASCII hyphen.
  s = s.replace(/[\u2010\u2011\u2012\u2013\u2014\u2015\u2212]/g, '-');
  // Ellipsis → three dots.
  s = s.replace(/\u2026/g, '...');
  // Non-breaking + thin/zero-width spaces → normal space / removed.
  s = s.replace(/[\u00A0\u2007\u202F]/g, ' ');
  s = s.replace(/[\u200B\u200C\u200D\uFEFF]/g, '');
  // Strip control chars except \n \r \t.
  s = s.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, ' ');
  return s;
}

function _ttsChunkText(text) {
  const t = _sanitizeTtsText(text).replace(/\s+/g, ' ').trim();
'@

$replaced = $text.Replace($marker, $helper)
if ($replaced -eq $text) {
  throw "Marker not found in $path - manual inspection required."
}

[System.IO.File]::WriteAllText($path, $replaced, (New-Object System.Text.UTF8Encoding($false)))
$after = [System.IO.File]::ReadAllText($path)
$has = $after -match 'function\s+_sanitizeTtsText\s*\('
Write-Host ("Patched. has_helper={0}  new_size={1} bytes" -f $has, $after.Length)
