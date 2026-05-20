#!/usr/bin/env python3
"""
Patch scripts/admin/_lib.mjs:
  - Rename existing aiTts -> _aiTtsRaw (internal, single-shot ≤512-char call).
  - Add new aiTts(text, opts) wrapper that splits text into ≤MAX_TTS_CHARS
    sentence/clause-bounded chunks and concatenates the resulting MP3 buffers.
Idempotent: safe to run multiple times.
"""
import sys, pathlib, re

path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '/opt/oetwebapp/scripts/admin/_lib.mjs')
s = path.read_text(encoding='utf-8')

if 'function _aiTtsRaw' in s:
    print('already patched, skipping')
    sys.exit(0)

# Step 1: rename existing `export async function aiTts(` to `async function _aiTtsRaw(`
old_decl = 'export async function aiTts(text, opts = {}) {'
new_decl = 'async function _aiTtsRaw(text, opts = {}) {'
assert old_decl in s, 'cannot find aiTts declaration'
s = s.replace(old_decl, new_decl, 1)

# Step 2: insert the new wrapper just before the next section comment line
# Use the section divider comment after the raw function as anchor.
anchor = '// -----------------------------------------------------------------------------\n// Chunked file upload (uses /v1/admin/uploads/* per ContentPapersAdminEndpoints)'
assert anchor in s, 'cannot find chunked-upload section anchor'

wrapper = '''
// -----------------------------------------------------------------------------
// aiTts wrapper: chunk long inputs (Qwen3-TTS hard cap is 512 chars/request).
//   Splits text on sentence boundaries (. ! ? then , then word) into ≤ 480-char
//   chunks, synthesizes each, then byte-concatenates the resulting MP3 buffers.
// -----------------------------------------------------------------------------
const TTS_MAX_CHARS = 480;

function _ttsChunkText(text) {
  const t = String(text || '').replace(/\\s+/g, ' ').trim();
  if (!t) return [];
  if (t.length <= TTS_MAX_CHARS) return [t];

  // Sentence-aware split: keep delimiter, then group sentences ≤ TTS_MAX_CHARS.
  const sentences = t.match(/[^.!?]+[.!?]+|[^.!?]+$/g) || [t];
  const chunks = [];
  let buf = '';
  for (const raw of sentences) {
    const sent = raw.trim();
    if (!sent) continue;
    if (sent.length > TTS_MAX_CHARS) {
      // Sentence itself is too long — split on commas then words.
      if (buf) { chunks.push(buf); buf = ''; }
      const pieces = sent.match(/[^,;:]+[,;:]+|[^,;:]+$/g) || [sent];
      let sub = '';
      for (const p of pieces) {
        const piece = p.trim();
        if (!piece) continue;
        if (piece.length > TTS_MAX_CHARS) {
          if (sub) { chunks.push(sub); sub = ''; }
          // Hard word-split.
          const words = piece.split(' ');
          let w = '';
          for (const word of words) {
            if ((w + ' ' + word).trim().length > TTS_MAX_CHARS) {
              if (w) chunks.push(w);
              w = word;
            } else {
              w = w ? w + ' ' + word : word;
            }
          }
          if (w) chunks.push(w);
        } else if ((sub + ' ' + piece).trim().length > TTS_MAX_CHARS) {
          if (sub) chunks.push(sub);
          sub = piece;
        } else {
          sub = sub ? sub + ' ' + piece : piece;
        }
      }
      if (sub) chunks.push(sub);
    } else if ((buf + ' ' + sent).trim().length > TTS_MAX_CHARS) {
      if (buf) chunks.push(buf);
      buf = sent;
    } else {
      buf = buf ? buf + ' ' + sent : sent;
    }
  }
  if (buf) chunks.push(buf);
  return chunks;
}

export async function aiTts(text, opts = {}) {
  const chunks = _ttsChunkText(text);
  if (chunks.length === 0) throw new Error('aiTts: empty text');
  if (chunks.length === 1) return _aiTtsRaw(chunks[0], opts);

  const buffers = [];
  for (let i = 0; i < chunks.length; i++) {
    const c = chunks[i];
    // small jitter between calls so we don't burst the rate-limiter
    if (i > 0) await sleep(150);
    const buf = await _aiTtsRaw(c, opts);
    buffers.push(buf);
  }
  // Naive byte concatenation. Qwen3-TTS emits CBR MP3 frames; concatenated
  // streams play correctly in browsers / audio decoders with at most a tiny
  // imperceptible gap at chunk boundaries — acceptable for practice material.
  return Buffer.concat(buffers);
}

'''

s = s.replace(anchor, wrapper + anchor, 1)
path.write_text(s, encoding='utf-8')
print('patched OK')
