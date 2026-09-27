/**
 * Display-safety helpers for agent output and approval commands.
 *
 * Everything the console renders comes from a model that may have read
 * attacker-controlled text (learner DB rows, logs, web pages, PR comments), so:
 * - invisible, control and bidirectional-override characters are made VISIBLE
 *   (Trojan-Source style reordering, zero-width smuggling, Unicode tag chars);
 * - base64 blobs in commands get a decoded preview so an approval never hides
 *   `echo … | base64 -d | sh` behind opaque text;
 * - markdown links and images are neutralised to plain text before rendering
 *   (no remote image fetches, no clickable javascript:/protocol-relative URLs).
 *
 * All output is plain strings/segments rendered by React (escaped); nothing
 * here produces HTML.
 */

export type RevealLevel = 'strict' | 'prose';

export type VisibleSegment =
  | { kind: 'text'; value: string }
  | { kind: 'char'; codePoint: number; label: string; name: string; category: HiddenCharCategory; lineBreak: boolean };

export type HiddenCharCategory = 'control' | 'bidi' | 'invisible' | 'space' | 'tag';

interface HiddenCharInfo {
  label: string;
  name: string;
  category: HiddenCharCategory;
}

const NAMED: Record<number, HiddenCharInfo> = {
  0x00: { label: 'NUL', name: 'NULL', category: 'control' },
  0x07: { label: 'BEL', name: 'BELL', category: 'control' },
  0x08: { label: 'BS', name: 'BACKSPACE', category: 'control' },
  0x09: { label: 'TAB', name: 'CHARACTER TABULATION', category: 'control' },
  0x0a: { label: 'LF', name: 'LINE FEED', category: 'control' },
  0x0b: { label: 'VT', name: 'LINE TABULATION', category: 'control' },
  0x0c: { label: 'FF', name: 'FORM FEED', category: 'control' },
  0x0d: { label: 'CR', name: 'CARRIAGE RETURN', category: 'control' },
  0x1b: { label: 'ESC', name: 'ESCAPE', category: 'control' },
  0x7f: { label: 'DEL', name: 'DELETE', category: 'control' },
  0x85: { label: 'NEL', name: 'NEXT LINE', category: 'control' },
  0xa0: { label: 'NBSP', name: 'NO-BREAK SPACE', category: 'space' },
  0xad: { label: 'SHY', name: 'SOFT HYPHEN', category: 'invisible' },
  0x034f: { label: 'CGJ', name: 'COMBINING GRAPHEME JOINER', category: 'invisible' },
  0x061c: { label: 'ALM', name: 'ARABIC LETTER MARK', category: 'bidi' },
  0x115f: { label: 'HCF', name: 'HANGUL CHOSEONG FILLER', category: 'invisible' },
  0x1160: { label: 'HJF', name: 'HANGUL JUNGSEONG FILLER', category: 'invisible' },
  0x180e: { label: 'MVS', name: 'MONGOLIAN VOWEL SEPARATOR', category: 'invisible' },
  0x200b: { label: 'ZWSP', name: 'ZERO WIDTH SPACE', category: 'invisible' },
  0x200c: { label: 'ZWNJ', name: 'ZERO WIDTH NON-JOINER', category: 'invisible' },
  0x200d: { label: 'ZWJ', name: 'ZERO WIDTH JOINER', category: 'invisible' },
  0x200e: { label: 'LRM', name: 'LEFT-TO-RIGHT MARK', category: 'bidi' },
  0x200f: { label: 'RLM', name: 'RIGHT-TO-LEFT MARK', category: 'bidi' },
  0x2028: { label: 'LSEP', name: 'LINE SEPARATOR', category: 'control' },
  0x2029: { label: 'PSEP', name: 'PARAGRAPH SEPARATOR', category: 'control' },
  0x202a: { label: 'LRE', name: 'LEFT-TO-RIGHT EMBEDDING', category: 'bidi' },
  0x202b: { label: 'RLE', name: 'RIGHT-TO-LEFT EMBEDDING', category: 'bidi' },
  0x202c: { label: 'PDF', name: 'POP DIRECTIONAL FORMATTING', category: 'bidi' },
  0x202d: { label: 'LRO', name: 'LEFT-TO-RIGHT OVERRIDE', category: 'bidi' },
  0x202e: { label: 'RLO', name: 'RIGHT-TO-LEFT OVERRIDE', category: 'bidi' },
  0x202f: { label: 'NNBSP', name: 'NARROW NO-BREAK SPACE', category: 'space' },
  0x205f: { label: 'MMSP', name: 'MEDIUM MATHEMATICAL SPACE', category: 'space' },
  0x2060: { label: 'WJ', name: 'WORD JOINER', category: 'invisible' },
  0x2066: { label: 'LRI', name: 'LEFT-TO-RIGHT ISOLATE', category: 'bidi' },
  0x2067: { label: 'RLI', name: 'RIGHT-TO-LEFT ISOLATE', category: 'bidi' },
  0x2068: { label: 'FSI', name: 'FIRST STRONG ISOLATE', category: 'bidi' },
  0x2069: { label: 'PDI', name: 'POP DIRECTIONAL ISOLATE', category: 'bidi' },
  0x3000: { label: 'IDSP', name: 'IDEOGRAPHIC SPACE', category: 'space' },
  0x3164: { label: 'HF', name: 'HANGUL FILLER', category: 'invisible' },
  0xfeff: { label: 'BOM', name: 'ZERO WIDTH NO-BREAK SPACE', category: 'invisible' },
  0xffa0: { label: 'HWHF', name: 'HALFWIDTH HANGUL FILLER', category: 'invisible' },
};

function formatCodePoint(codePoint: number): string {
  return `U+${codePoint.toString(16).toUpperCase().padStart(4, '0')}`;
}

/**
 * Classify a code point that must not be rendered as-is. Returns null for
 * ordinary visible characters. `prose` keeps tab/newline and the joiners that
 * legitimately appear in emoji sequences; `strict` (commands) reveals all.
 */
export function classifyHiddenChar(codePoint: number, level: RevealLevel = 'strict'): HiddenCharInfo | null {
  if (level === 'prose') {
    if (codePoint === 0x09 || codePoint === 0x0a || codePoint === 0x0d) return null;
    if (codePoint === 0x200d || codePoint === 0x200c) return null;
    if (codePoint >= 0xfe00 && codePoint <= 0xfe0f) return null; // emoji variation selectors
    if (codePoint === 0xa0 || codePoint === 0x202f || codePoint === 0x3000) return null;
  }

  const named = NAMED[codePoint];
  if (named) return named;

  if (codePoint <= 0x1f || (codePoint >= 0x80 && codePoint <= 0x9f)) {
    return { label: codePoint <= 0x1f ? 'C0' : 'C1', name: 'CONTROL CHARACTER', category: 'control' };
  }
  if (codePoint >= 0x2000 && codePoint <= 0x200a) {
    return { label: 'SP', name: 'UNUSUAL SPACE', category: 'space' };
  }
  if (codePoint >= 0x2061 && codePoint <= 0x2064) {
    return { label: 'INV', name: 'INVISIBLE OPERATOR', category: 'invisible' };
  }
  if (codePoint >= 0x206a && codePoint <= 0x206f) {
    return { label: 'DEPR', name: 'DEPRECATED FORMAT CHARACTER', category: 'bidi' };
  }
  if ((codePoint >= 0xfe00 && codePoint <= 0xfe0f) || (codePoint >= 0xe0100 && codePoint <= 0xe01ef)) {
    return { label: 'VS', name: 'VARIATION SELECTOR', category: 'invisible' };
  }
  if (codePoint >= 0xe0000 && codePoint <= 0xe007f) {
    return { label: 'TAG', name: 'TAG CHARACTER', category: 'tag' };
  }
  if (codePoint >= 0xfff9 && codePoint <= 0xfffb) {
    return { label: 'IAN', name: 'INTERLINEAR ANNOTATION', category: 'invisible' };
  }
  return null;
}

/** Split text into visible runs and markers for every hidden/control character. */
export function revealHiddenCharacters(text: string, level: RevealLevel = 'strict'): VisibleSegment[] {
  const segments: VisibleSegment[] = [];
  if (!text) return segments;
  let buffer = '';
  for (const ch of text) {
    const codePoint = ch.codePointAt(0) ?? 0;
    const info = classifyHiddenChar(codePoint, level);
    if (!info) {
      buffer += ch;
      continue;
    }
    if (buffer) {
      segments.push({ kind: 'text', value: buffer });
      buffer = '';
    }
    segments.push({
      kind: 'char',
      codePoint,
      label: `${formatCodePoint(codePoint)} ${info.label}`,
      name: info.name,
      category: info.category,
      lineBreak: codePoint === 0x0a || codePoint === 0x2028 || codePoint === 0x2029,
    });
  }
  if (buffer) segments.push({ kind: 'text', value: buffer });
  return segments;
}

/** String form: hidden characters become `‹U+202E RLO›` tokens (newlines kept in prose). */
export function replaceHiddenCharacters(text: string, level: RevealLevel = 'prose'): string {
  if (!text) return '';
  let out = '';
  for (const segment of revealHiddenCharacters(text, level)) {
    out += segment.kind === 'text' ? segment.value : `‹${segment.label}›${segment.lineBreak ? '\n' : ''}`;
  }
  return out;
}

export function countHiddenCharacters(text: string, level: RevealLevel = 'strict'): number {
  let count = 0;
  for (const segment of revealHiddenCharacters(text, level)) {
    if (segment.kind === 'char' && !(segment.lineBreak && segment.codePoint === 0x0a)) count += 1;
  }
  return count;
}

/** Strong right-to-left letters (Hebrew, Arabic, Syriac, Thaana, NKo, presentation forms). */
export function hasRightToLeftCharacters(text: string): boolean {
  for (const ch of text ?? '') {
    const cp = ch.codePointAt(0) ?? 0;
    if (
      (cp >= 0x0590 && cp <= 0x08ff)
      || (cp >= 0xfb1d && cp <= 0xfdff)
      || (cp >= 0xfe70 && cp <= 0xfefe)
      || (cp >= 0x10800 && cp <= 0x10fff)
      || (cp >= 0x1e800 && cp <= 0x1efff)
    ) {
      return true;
    }
  }
  return false;
}

export function countNonAscii(text: string): number {
  let count = 0;
  for (const ch of text ?? '') {
    if ((ch.codePointAt(0) ?? 0) > 0x7e) count += 1;
  }
  return count;
}

// ─── Base64 preview ─────────────────────────────────────────────────────────

export interface DecodedBase64Segment {
  encoded: string;
  decoded: string;
}

const BASE64_CANDIDATE = /[A-Za-z0-9+/_-]{8,}={0,2}/g;
const MAX_BASE64_SEGMENTS = 8;
const MAX_ENCODED_LENGTH = 16_384;

function decodeBase64(candidate: string): string | null {
  const normalized = candidate.replace(/-/g, '+').replace(/_/g, '/');
  const unpadded = normalized.replace(/=+$/, '');
  if (unpadded.length % 4 === 1) return null;
  const padded = unpadded + '='.repeat((4 - (unpadded.length % 4)) % 4);
  if (typeof atob !== 'function') return null;
  let binary: string;
  try {
    binary = atob(padded);
  } catch {
    return null;
  }
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

function looksLikeText(decoded: string): boolean {
  if (decoded.length < 4) return false;
  let unprintable = 0;
  let total = 0;
  for (const ch of decoded) {
    total += 1;
    const cp = ch.codePointAt(0) ?? 0;
    if (cp === 0) return false;
    const isWhitespace = cp === 0x09 || cp === 0x0a || cp === 0x0d;
    if (!isWhitespace && (cp < 0x20 || (cp >= 0x7f && cp <= 0x9f) || cp === 0xfffd)) unprintable += 1;
  }
  return total > 0 && unprintable / total <= 0.1;
}

/**
 * Find base64/base64url runs whose decoding is readable UTF-8 text. Plain
 * words and identifiers decode to binary garbage and are skipped.
 */
export function findDecodableBase64(text: string): DecodedBase64Segment[] {
  const results: DecodedBase64Segment[] = [];
  if (!text) return results;
  const seen = new Set<string>();
  const matches = text.match(BASE64_CANDIDATE) ?? [];
  for (const match of matches) {
    if (results.length >= MAX_BASE64_SEGMENTS) break;
    if (match.length > MAX_ENCODED_LENGTH || seen.has(match)) continue;
    seen.add(match);
    // Pure lowercase/uppercase words and plain paths are almost never payloads.
    if (/^[a-z_-]+$/.test(match) || /^[A-Z_-]+$/.test(match)) continue;
    const decoded = decodeBase64(match);
    if (decoded !== null && looksLikeText(decoded)) {
      results.push({ encoded: match, decoded });
    }
  }
  return results;
}

// ─── Markdown neutralisation ────────────────────────────────────────────────

const IMAGE_PATTERN = /!\[([^\]\n]*)\]\(([^)\n]*)\)/g;
const LINK_PATTERN = /\[([^\]\n]+)\]\(([^)\n]+)\)/g;

function neutralizeInline(segment: string): string {
  return segment
    .replace(IMAGE_PATTERN, (_match, alt: string, src: string) => `[image${alt ? `: ${alt}` : ''}] (${src.trim()})`)
    .replace(LINK_PATTERN, (_match, label: string, href: string) => `${label} (${href.trim()})`);
}

function neutralizeLine(line: string): string {
  // Leave inline code spans untouched; rewrite links/images everywhere else.
  let out = '';
  let index = 0;
  while (index < line.length) {
    const tick = line.indexOf('`', index);
    if (tick === -1) {
      out += neutralizeInline(line.slice(index));
      break;
    }
    const close = line.indexOf('`', tick + 1);
    if (close === -1) {
      out += neutralizeInline(line.slice(index));
      break;
    }
    out += neutralizeInline(line.slice(index, tick));
    out += line.slice(tick, close + 1);
    index = close + 1;
  }
  return out;
}

/**
 * Prepare untrusted assistant markdown for `MarkdownContent`:
 * - hidden/bidi/control characters become visible tokens (code included);
 * - `[label](url)` → `label (url)` and `![alt](src)` → `[image: alt] (src)`,
 *   outside code, so nothing becomes a clickable link or a remote image;
 * - as a backstop, every remaining `](` outside fenced code becomes `] (`.
 *   MarkdownContent's link parser needs that exact adjacency, and its inline
 *   code / paragraph handling can join text across the boundaries the line
 *   rewrite above sees (e.g. a backtick inside the URL, or a label split over
 *   two lines), so without this a crafted message could still yield a link.
 * Fences are detected exactly as MarkdownContent does (a line starting with
 * three backticks); fenced code is rendered verbatim there and never linked.
 * Raw HTML needs no treatment: MarkdownContent renders it as escaped text.
 */
export function toSafeConsoleMarkdown(markdown: string): string {
  if (!markdown) return '';
  const visible = replaceHiddenCharacters(markdown, 'prose');
  const lines = visible.replace(/\r\n/g, '\n').split('\n');
  let inFence = false;
  return lines
    .map((line) => {
      if (line.startsWith('```')) {
        inFence = !inFence;
        return line;
      }
      return inFence ? line : neutralizeLine(line).replace(/\]\(/g, '] (');
    })
    .join('\n');
}

// ─── URLs ───────────────────────────────────────────────────────────────────

/**
 * Return the URL only when it is https and its host is (a subdomain of) one of
 * `allowedHosts`. Used for vendor sign-in links, PR and Actions links, so a
 * compromised sidecar cannot turn them into javascript: or look-alike links.
 */
export function safeExternalUrl(value: string | null | undefined, allowedHosts: readonly string[]): string | null {
  if (!value || typeof value !== 'string') return null;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' || url.username || url.password) return null;
  const host = url.hostname.toLowerCase();
  const allowed = allowedHosts.some((allowedHost) => {
    const normalized = allowedHost.toLowerCase();
    return host === normalized || host.endsWith(`.${normalized}`);
  });
  return allowed ? url.toString() : null;
}

export const ANTHROPIC_SIGN_IN_HOSTS: readonly string[] = ['claude.ai', 'claude.com', 'anthropic.com'];
export const OPENAI_SIGN_IN_HOSTS: readonly string[] = ['openai.com', 'chatgpt.com'];
export const GITHUB_HOSTS: readonly string[] = ['github.com'];
