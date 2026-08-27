/**
 * Listening B/C question prompt cleaning.
 * Strips sentinel placeholders (See PDF/CPDF) and imported PDF extraction artifacts
 * so candidate-facing text never shows them. Mirrors Part A cleaner
 * `lib/listening-part-a-notes.ts:219` but for B/C prompts/options.
 *
 * Keep regexes anchored / fail-closed — only strip lines that are clearly
 * artifacts, never legitimate clinical question content.
 */

const SENTINEL_RE = /^(see pdf|cpdf|pdf|view pdf)$/i;

// Artifact line patterns — each matches a whole line (or leading fragment)
// that is an extraction header/footer, not question content.
const ARTIFACT_LINE_RES = [
  // `--- PAGE 4 ---` / `====== PAGE 4 ======` / `----- PAGE 4 -----`
  /^\s*[-=]{2,}\s*PAGE\s*\d+\s*[-=]{2,}\s*$/i,
  // Bare `PAGE 4` / `PAGE 12` on its own line
  /^\s*PAGE\s*\d+\s*$/i,
  // `Practice Test 1` / `Practice Test 10 :` / `Practice Test 16 :`
  /^\s*Practice Test\s*\d+\s*:?\s*$/i,
  // Word bullet glyph from PDF extraction (\uF0B7)
  /^\s*\uF0B7\s*$/,
];

const INLINE_ARTIFACT_RES = [
  // Leading `PAGE 4` fragment before real question (e.g. "PAGE 4 Question 25 ...")
  /^\s*PAGE\s+\d+\s+/i,
  /^\s*[-=]{2,}\s*PAGE\s+\d+\s*[-=]{2,}\s*/i,
  /^\s*Practice Test\s*\d+\s*[:\-]?\s*/i,
];

/**
 * Clean a listening question prompt for candidate display.
 * Returns "" for sentinel-only prompts so the caller can decide fallback
 * (DB fix should have replaced sentinel with real prompt; "" avoids rendering "See PDF").
 */
export function cleanListeningPrompt(raw: string | null | undefined): string {
  if (raw == null) return '';
  let text = String(raw).trim();
  if (!text) return '';
  // Exact sentinel — do not render
  if (SENTINEL_RE.test(text)) return '';

  // Strip sentinel appearing as isolated line inside multi-line extraction
  const lines = text.split(/\r?\n/);
  const filtered = lines.filter((line) => {
    const trimmed = line.trim();
    if (!trimmed) return false;
    if (SENTINEL_RE.test(trimmed)) return false;
    for (const re of ARTIFACT_LINE_RES) {
      if (re.test(trimmed)) return false;
    }
    return true;
  });
  if (filtered.length === 0) return '';
  text = filtered.join(' ').trim();

  // Strip leading inline artifact fragment (e.g. "PAGE 4 " prefix)
  for (const re of INLINE_ARTIFACT_RES) {
    text = text.replace(re, '').trim();
  }

  // Strip trailing/inline artifacts that appear as suffixes (e.g. "common there. ===== PAGE 4 =====" or "moment. o Practice Test 1 :")
  text = text.replace(/\s*={2,}\s*PAGE\s*\d+\s*={2,}\s*/gi, ' ');
  text = text.replace(/\s*-{2,}\s*PAGE\s*\d+\s*-{2,}\s*/gi, ' ');
  text = text.replace(/\s*PAGE\s*\d+\s*/gi, ' ');
  text = text.replace(/\s*o\s*Practice Test\s*\d+\s*:?\s*/gi, ' ');
  text = text.replace(/\s*Practice Test\s*\d+\s*:?\s*/gi, ' ');
  text = text.replace(/\uF0B7/g, ' ');

  // Collapse whitespace
  text = text.replace(/\s{2,}/g, ' ').trim();
  if (SENTINEL_RE.test(text)) return '';
  return text;
}

export const sanitizeListeningPrompt = cleanListeningPrompt;

const OPTION_PLACEHOLDER_RE = /^Option\s+[ABC]$/i;

export function cleanListeningOption(raw: string | null | undefined): string {
  if (raw == null) return '';
  let text = String(raw).trim();
  if (!text) return '';
  if (OPTION_PLACEHOLDER_RE.test(text)) return '';
  if (SENTINEL_RE.test(text)) return '';

  const lines = text.split(/\r?\n/);
  const filtered = lines.filter((line) => {
    const trimmed = line.trim();
    if (!trimmed) return false;
    if (SENTINEL_RE.test(trimmed)) return false;
    if (OPTION_PLACEHOLDER_RE.test(trimmed)) return false;
    for (const re of ARTIFACT_LINE_RES) {
      if (re.test(trimmed)) return false;
    }
    return true;
  });
  if (filtered.length === 0) return '';
  text = filtered.join(' ').trim();

  for (const re of INLINE_ARTIFACT_RES) {
    text = text.replace(re, '').trim();
  }
  text = text.replace(/\s*={2,}\s*PAGE\s*\d+\s*={2,}\s*/gi, ' ');
  text = text.replace(/\s*-{2,}\s*PAGE\s*\d+\s*-{2,}\s*/gi, ' ');
  text = text.replace(/\s*PAGE\s*\d+\s*/gi, ' ');
  text = text.replace(/\s*o\s*Practice Test\s*\d+\s*:?\s*/gi, ' ');
  text = text.replace(/\s*Practice Test\s*\d+\s*:?\s*/gi, ' ');
  text = text.replace(/\uF0B7/g, ' ');
  text = text.replace(/\s{2,}/g, ' ').trim();
  if (OPTION_PLACEHOLDER_RE.test(text)) return '';
  if (SENTINEL_RE.test(text)) return '';
  return text.trim();
}

export const sanitizeListeningOption = cleanListeningOption;

export function isSentinelPrompt(raw: string | null | undefined): boolean {
  if (raw == null) return false;
  return SENTINEL_RE.test(String(raw).trim());
}
