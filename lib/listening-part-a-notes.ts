/**
 * OET Listening Part A — note-completion document grammar.
 *
 * Single source of truth for:
 *   - The canonical gap marker constant.
 *   - The `NotesNode` / `NotesSegment` type hierarchy.
 *   - `parseNotesDocument` — parse a notes body into renderable nodes.
 *   - `countGaps` — count gap markers in a body.
 *   - `detectPastedGaps` — clean raw pasted OET notes into a canonical body.
 *   - `sanitizePastedStem` — sanitize pasted rich content (moved from PartANotesBuilder).
 *
 * No React. `parseNotesDocument` and `countGaps` are pure string functions.
 * Only `detectPastedGaps` / `sanitizePastedStem` may call the sanitizer.
 *
 * Notes-document grammar: the ONLY gap marker recognised in this module is a
 * run of 4+ underscores (`____`). `detectPastedGaps` and the authoring toolbar
 * always emit this canonical form. Legacy `[ ]` / `{{ blank }}` markers belong
 * only to old per-question stems rendered by `PartARenderer` (a separate
 * concern — do not change `PartARenderer`).
 */

import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

// ── Constants ──────────────────────────────────────────────────────────────────

/** Canonical gap marker emitted by the authoring toolbar and expected by the renderer. */
export const PART_A_GAP_MARKER = '____';

/**
 * Canonical OET Listening Part A note-completion skeleton, inserted by the
 * "Scaffold" toolbar button. It demonstrates every construct the grammar
 * understands — the boilerplate intro line, the "thirty seconds" reading-time
 * line, the `Patient:` label line, two `## ` section headings, level-1 bullets,
 * a level-2 sub-bullet, a mid-sentence gap, and a gap with trailing text — so a
 * data-entry author starts from the right shape and only replaces the
 * placeholders. The `[...]` placeholders are literal text (the notes grammar
 * only treats a run of 4+ underscores as a gap), so they render as obvious
 * "replace me" prompts and never become answer blanks.
 *
 * Lives in this pure module (rather than the React builder) so both the legacy
 * textarea builder and the TipTap editor can import it without a circular
 * dependency.
 */
export const PART_A_SCAFFOLD = [
  'You hear a [professional, e.g. primary-care doctor] talking to a patient called [patient name]. For questions 1–12, complete the notes with a word or short phrase that you hear.',
  '',
  'You now have thirty seconds to look at the notes.',
  '',
  'Patient: [patient name]',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '- [note detail], ____ and [trailing detail]',
  '  - [sub-detail] ____',
  '',
  '## [Section heading]',
  '- [note detail] ____',
  '',
].join('\n');

/**
 * Shared regex that recognises the notes-document gap notation: a run of 4+
 * underscores. Used by BOTH `parseNotesDocument` (segment splitting) and
 * `countGaps` so the two functions always agree on what constitutes a gap.
 *
 * The capturing group is required so `String.split(BLANK_PATTERN)` keeps the
 * matched separator tokens in the result array (allowing `splitLineIntoSegments`
 * to distinguish gap parts from text parts).
 *
 * Note: legacy `[ ]` / `{{ blank }}` are intentionally NOT matched here; those
 * belong to old per-question stems handled by `PartARenderer`.
 */
const BLANK_PATTERN = /(_{4,})/;

// ── Types ──────────────────────────────────────────────────────────────────────

export type NotesSegment = { kind: 'text'; text: string } | { kind: 'gap'; gapIndex: number };

export type NotesNode =
  | { kind: 'heading'; segments: NotesSegment[] }
  | { kind: 'subheading'; segments: NotesSegment[] }
  | { kind: 'bullet'; level: 1 | 2; segments: NotesSegment[] }
  | { kind: 'context'; segments: NotesSegment[] }
  | { kind: 'divider' };

// ── Internal helpers ───────────────────────────────────────────────────────────

/**
 * Split one line of text into NotesSegment[], assigning global gap ordinals
 * starting from `gapCounter.value` and incrementing it for each gap found.
 */
function splitLineIntoSegments(
  text: string,
  gapCounter: { value: number },
): NotesSegment[] {
  const parts = text.split(BLANK_PATTERN).filter((p) => p.length > 0);
  const segments: NotesSegment[] = [];
  for (const part of parts) {
    if (BLANK_PATTERN.test(part)) {
      segments.push({ kind: 'gap', gapIndex: gapCounter.value++ });
    } else {
      segments.push({ kind: 'text', text: part });
    }
  }
  return segments;
}

// ── Public API ─────────────────────────────────────────────────────────────────

/**
 * Parse a notes body into ordered `NotesNode[]`.
 *
 * Line rules (evaluated in order):
 *   - `### ` prefix → sub-heading
 *   - `## ` prefix → heading
 *   - `  - ` or `\t- ` prefix → bullet level 2 (sub-bullet, capped at level 2)
 *   - `- ` prefix → bullet level 1
 *   - exactly `---` (trimmed) → divider
 *   - empty / whitespace-only → skipped (paragraph break)
 *   - any other non-empty line → context
 *
 * Gap markers in heading/bullet/context lines get globally ordered `gapIndex`
 * values (0-based, counting across the whole document in top-to-bottom order).
 */
export function parseNotesDocument(body: string | null | undefined): NotesNode[] {
  if (!body) return [];

  // Normalize line endings: CRLF (\r\n) and lone CR (\r) → LF (\n).
  // This prevents a trailing \r from leaking into segment text when the input
  // comes from Windows clipboard pastes or files with CRLF encoding.
  const normalized = body.replace(/\r\n?/g, '\n');
  const lines = normalized.split('\n');
  const nodes: NotesNode[] = [];
  const gapCounter = { value: 0 };

  for (const line of lines) {
    // Empty / whitespace-only → paragraph break, no node.
    if (line.trim().length === 0) continue;

    // Divider: exactly `---` (trimmed).
    if (line.trim() === '---') {
      nodes.push({ kind: 'divider' });
      continue;
    }

    // Sub-heading: `### ` prefix. Checked before `## ` (they don't collide —
    // `'### '.startsWith('## ')` is false — but ordering keeps intent obvious).
    if (line.startsWith('### ')) {
      const text = line.slice(4);
      nodes.push({ kind: 'subheading', segments: splitLineIntoSegments(text, gapCounter) });
      continue;
    }

    // Heading: `## ` prefix.
    if (line.startsWith('## ')) {
      const text = line.slice(3);
      nodes.push({ kind: 'heading', segments: splitLineIntoSegments(text, gapCounter) });
      continue;
    }

    // Sub-bullet (level 2): two spaces or a tab then `- `.
    if (line.startsWith('  - ') || line.startsWith('\t- ')) {
      const text = line.startsWith('\t- ') ? line.slice(3) : line.slice(4);
      nodes.push({ kind: 'bullet', level: 2, segments: splitLineIntoSegments(text, gapCounter) });
      continue;
    }

    // Bullet (level 1): `- ` prefix.
    if (line.startsWith('- ')) {
      const text = line.slice(2);
      nodes.push({ kind: 'bullet', level: 1, segments: splitLineIntoSegments(text, gapCounter) });
      continue;
    }

    // Context line: any other non-empty line (including a gap-only line like
    // "____"). A gap-only line is intentionally a `context` node — it produces
    // a paragraph that holds a single inline gap, which is a valid layout choice
    // for note-completion documents.
    nodes.push({ kind: 'context', segments: splitLineIntoSegments(line, gapCounter) });
  }

  return nodes;
}

/**
 * Count gap markers in a body. Each run of 4+ underscores is ONE gap.
 * `'____ ____'` (separated) = 2; `'________'` (contiguous) = 1.
 * Returns 0 for null / undefined / empty strings.
 *
 * Uses the same `BLANK_PATTERN` definition as `parseNotesDocument` /
 * `splitLineIntoSegments` so the two functions always agree on gap count.
 */
export function countGaps(body: string | null | undefined): number {
  if (!body) return 0;
  // Use a global version of BLANK_PATTERN (which uses a capturing group) to
  // count occurrences. The global flag is applied here to avoid polluting the
  // shared constant with state.
  const matches = body.match(/_{4,}/g);
  return matches ? matches.length : 0;
}

/**
 * Paste-parser: clean raw pasted text/HTML from official OET notes into a
 * canonical body and report the gap count.
 *
 * Steps:
 *   1. Sanitize via `sanitizePastedStem` (neutralise scripts, preserve line
 *      structure).
 *   2. Replace `(n)` ONLY when immediately followed (with optional spaces/tabs)
 *      by an underscore run → `____`. A bare `(n)` with no following underscores
 *      is left unchanged so clinical notation like "Stage (3) cancer" is
 *      preserved and the admin can refine via the toolbar.
 *   3. Normalize any remaining standalone underscore runs (4+) → `____`.
 *   4. Return cleaned body + `countGaps` result.
 */
export function detectPastedGaps(raw: string): { body: string; gapCount: number } {
  // Step 1: sanitize.
  let body = sanitizePastedStem(raw);

  // Step 2: (n) + optional spaces/tabs + an underscore run → canonical gap.
  // A bare "(n)" with NO following underscores is left alone (the admin refines
  // via toolbar). This prevents word-fusing in text like "Stage (3) cancer".
  body = body.replace(/\((\d+)\)[ \t]*_+/g, PART_A_GAP_MARKER);

  // Step 3: any remaining standalone run of 4+ underscores → canonical marker.
  body = body.replace(/_{4,}/g, PART_A_GAP_MARKER);

  return { body, gapCount: countGaps(body) };
}

/**
 * Strip author-pasted rich content down to safe plain text. The stem is a
 * plain-text-with-gap-markers field (never rendered as raw HTML), so we route
 * the paste through the repo sanitizer first to neutralise scripts / handlers,
 * then drop any surviving tags and decode the handful of entities a paste from
 * a word processor or browser typically carries.
 *
 * Moved verbatim from `PartANotesBuilder.tsx` — behavior is identical.
 */
export function sanitizePastedStem(raw: string): string {
  const safe = sanitizeBodyHtml(raw);
  const withoutTags = safe
    // Treat block-level boundaries as newlines so pasted lists/paragraphs keep
    // their structure instead of collapsing onto one line.
    .replace(/<\/(?:p|div|li|h[1-6]|tr|blockquote)>/gi, '\n')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '');
  const decoded = withoutTags
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&#39;/gi, "'");
  // Collapse runs of 3+ blank lines a sanitized paste can leave behind.
  return decoded.replace(/\n{3,}/g, '\n\n');
}
