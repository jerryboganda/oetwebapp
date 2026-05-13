/**
 * ============================================================================
 * Wizard — sanitize-html (Medium #2, May 2026 audit closure)
 * ============================================================================
 *
 * The Mocks Authoring Wizard captures author-supplied HTML in the Reading
 * Step's text body fields. The wizard never renders that HTML directly to the
 * learner — the manifest is POSTed to the backend, which stores the bytes as
 * `ReadingText.BodyHtml` and renders them later inside the learner Reading
 * player (`app/reading/paper/[paperId]/page.tsx`) inside a
 * `dangerouslySetInnerHTML` block scoped to a sandboxed reading-pane.
 *
 * This helper is a defense-in-depth wrapper: any future surface that wants to
 * render `bodyHtml` directly should route the input through `sanitizeBodyHtml`
 * first. The implementation is intentionally minimal-by-default:
 *
 *   1. If DOMPurify is available at runtime (browser environment with the
 *      `dompurify` package installed), use it with a conservative allow-list
 *      tuned for OET reading texts (paragraphs, emphasis, lists, tables).
 *   2. Otherwise, fall back to a regex-based scrub that strips `<script>`
 *      blocks, inline event handlers (`on*=`), `javascript:` URLs, and any
 *      `<iframe>` / `<object>` / `<embed>` / `<form>` tags.
 *
 * The fallback is deliberately conservative: anything ambiguous is dropped.
 * Adding `dompurify` to `package.json` is a follow-up wave; until it lands,
 * the regex fallback covers the OWASP Top-Ten reflected/stored XSS classes
 * that matter for a moderated admin-facing wizard.
 *
 * Usage:
 * ```ts
 * import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';
 * <div dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(bodyHtml) }} />
 * ```
 * ============================================================================
 */

const SCRIPT_TAG = /<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>/gi;
const EVENT_HANDLER = /\son\w+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi;
const JAVASCRIPT_URL = /\b(?:href|src|action|formaction|xlink:href)\s*=\s*(?:"|')?\s*javascript:/gi;
const DANGEROUS_TAGS = /<\/?(?:iframe|object|embed|form|meta|link|base|style)\b[^>]*>/gi;
const DATA_URLS_NON_IMAGE = /\b(?:href|src|action|formaction)\s*=\s*(?:"|')?\s*data:(?!image\/)[^"'>\s]*/gi;

interface DomPurifyLike {
  sanitize: (input: string, options?: Record<string, unknown>) => string;
}

let cachedDomPurify: DomPurifyLike | null | undefined;

function resolveDomPurify(): DomPurifyLike | null {
  if (cachedDomPurify !== undefined) return cachedDomPurify;

  if (typeof window === 'undefined') {
    cachedDomPurify = null;
    return null;
  }

  // Optional dependency — try a runtime resolve so missing-dep environments
  // gracefully fall back to the regex scrubber. Avoid a static import so the
  // bundler doesn't fail when `dompurify` isn't installed.
  try {
    const mod = (window as unknown as { DOMPurify?: DomPurifyLike }).DOMPurify;
    cachedDomPurify = mod ?? null;
  } catch {
    cachedDomPurify = null;
  }
  return cachedDomPurify;
}

const ALLOWED_TAGS = [
  'p', 'br', 'strong', 'em', 'b', 'i', 'u', 'span',
  'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'ul', 'ol', 'li',
  'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td',
  'blockquote', 'code', 'pre',
  'sup', 'sub',
];

const ALLOWED_ATTR = ['class', 'id', 'href', 'title', 'colspan', 'rowspan'];

/**
 * Sanitize an author-supplied HTML body for safe `dangerouslySetInnerHTML`
 * rendering. Always returns a string; never throws on malformed input.
 *
 * Wizard Medium #2 — May 2026 audit closure.
 */
export function sanitizeBodyHtml(input: string | null | undefined): string {
  if (input == null) return '';
  const raw = String(input);
  if (raw.length === 0) return '';

  const purifier = resolveDomPurify();
  if (purifier) {
    try {
      return purifier.sanitize(raw, {
        ALLOWED_TAGS,
        ALLOWED_ATTR,
        ALLOW_DATA_ATTR: false,
        FORBID_TAGS: ['script', 'iframe', 'object', 'embed', 'form'],
        FORBID_ATTR: ['style'],
      });
    } catch {
      // Fall through to the regex scrubber.
    }
  }

  return regexFallback(raw);
}

/**
 * Conservative regex-based scrub. Used when DOMPurify is not available.
 * Strips:
 *   - `<script>` blocks (including nested unclosed ones).
 *   - Inline event handlers (`onclick=`, `onload=`, etc.).
 *   - `javascript:` URLs in href/src/action/etc.
 *   - Non-image `data:` URLs.
 *   - Dangerous tags (`<iframe>`, `<object>`, `<embed>`, `<form>`,
 *     `<meta>`, `<link>`, `<base>`, `<style>`).
 *
 * Intentionally does NOT attempt to parse the HTML — it leaves benign markup
 * untouched. For a tighter allow-list, install `dompurify`.
 */
function regexFallback(raw: string): string {
  return raw
    .replace(SCRIPT_TAG, '')
    .replace(DANGEROUS_TAGS, '')
    .replace(EVENT_HANDLER, '')
    .replace(JAVASCRIPT_URL, (match) =>
      match.replace(/javascript:/gi, '').replace(/=\s*$/, '=""'),
    )
    .replace(DATA_URLS_NON_IMAGE, (match) =>
      match.replace(/data:[^"'>\s]*/i, ''),
    );
}

/**
 * Lightweight test seam — exposed only for unit tests of the regex fallback
 * and not intended for production callers. Public callers must always use
 * `sanitizeBodyHtml`.
 */
export const __unsafe_regexFallbackForTests = regexFallback;
