$path = 'scripts/admin/_lib.mjs'
$nl = "`r`n"
# Check actual line endings
$bytes = [System.IO.File]::ReadAllBytes($path)
$hasCRLF = $false
for ($i = 0; $i -lt [Math]::Min($bytes.Length-1, 2000); $i++) {
  if ($bytes[$i] -eq 13 -and $bytes[$i+1] -eq 10) { $hasCRLF = $true; break }
}
if (-not $hasCRLF) { $nl = "`n" }
Write-Host "Line endings: $(if($hasCRLF){'CRLF'}else{'LF'})"

$lines = [System.IO.File]::ReadAllLines($path)
Write-Host "Total lines before: $($lines.Count)"

$newFunc = @(
'export async function uploadMediaAsset(buf, { filename, mimeType, kind = ''document'', intendedRole = null } = {}) {',
'  if (!filename) throw new Error(''uploadMediaAsset: filename required'');',
'  if (!mimeType) throw new Error(''uploadMediaAsset: mimeType required'');',
'',
'  // Map orchestrator `kind` to backend PaperAssetRole. Callers may override via intendedRole.',
'  const role = intendedRole || (kind === ''audio'' ? ''Audio'' : ''Supplementary'');',
'',
'  // 1. Start session: POST /v1/admin/uploads',
'  //    ChunkedUploadStartDto { OriginalFilename, DeclaredMimeType, DeclaredSizeBytes, IntendedRole }',
'  //    -> { uploadId, chunkSizeBytes, expiresAt }',
'  const start = await adminFetch(''/v1/admin/uploads'', {',
'    method: ''POST'',',
'    body: {',
'      originalFilename: filename,',
'      declaredMimeType: mimeType,',
'      declaredSizeBytes: buf.length,',
'      intendedRole: role,',
'    },',
'  });',
'  if (!start.ok) {',
'    throw new Error(`uploads start failed (${start.status}): ${JSON.stringify(start.data).slice(0, 400)}`);',
'  }',
'  const uploadId = start.data.uploadId;',
'  const chunkSize = Number(start.data.chunkSizeBytes) || 8 * 1024 * 1024;',
'  if (!uploadId) throw new Error(`uploads start returned no uploadId: ${JSON.stringify(start.data).slice(0, 400)}`);',
'',
'  // 2. Upload parts: PUT /v1/admin/uploads/{uploadId}/parts/{partNumber:int}',
'  //    Body is raw application/octet-stream. partNumber is 1-based.',
'  let offset = 0;',
'  let partNumber = 1;',
'  while (offset < buf.length) {',
'    const end = Math.min(offset + chunkSize, buf.length);',
'    const slice = buf.subarray(offset, end);',
'    const r = await adminFetch(`/v1/admin/uploads/${uploadId}/parts/${partNumber}`, {',
'      method: ''PUT'',',
'      body: slice,',
'      headers: { ''Content-Type'': ''application/octet-stream'' },',
'    });',
'    if (!r.ok) {',
'      throw new Error(`uploads ${uploadId} part ${partNumber} failed (${r.status}): ${typeof r.data === ''string'' ? r.data.slice(0, 200) : JSON.stringify(r.data).slice(0, 200)}`);',
'    }',
'    offset = end;',
'    partNumber++;',
'  }',
'',
'  // 3. Complete: POST /v1/admin/uploads/{uploadId}/complete',
'  //    -> ChunkedUploadCommitResult { mediaAssetId, sha256, sizeBytes, deduplicated }',
'  const complete = await adminFetch(`/v1/admin/uploads/${uploadId}/complete`, { method: ''POST'', body: {} });',
'  if (!complete.ok) {',
'    throw new Error(`uploads ${uploadId} complete failed (${complete.status}): ${JSON.stringify(complete.data).slice(0, 400)}`);',
'  }',
'  const assetId = complete.data.mediaAssetId;',
'  if (!assetId) throw new Error(`uploads complete returned no mediaAssetId: ${JSON.stringify(complete.data).slice(0, 400)}`);',
'  return assetId;',
'}'
)

# Replace lines 609..651 (1-based) = indices 608..650
$before = $lines[0..607]
$after = $lines[651..($lines.Count-1)]
$merged = @($before) + @($newFunc) + @($after)
Write-Host "Total lines after: $($merged.Count)"

# Write with original line endings, no trailing extra newline
$content = ($merged -join $nl) + $nl
[System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))

Write-Host "Done. New size: $((Get-Item $path).Length) bytes"
