import { describe, expect, it } from 'vitest';
import { sanitizeBodyHtml, __unsafe_regexFallbackForTests as regexFallback } from '../sanitize-html';

/**
 * Wizard Medium #2 (May 2026 audit closure).
 *
 * `sanitizeBodyHtml` is a defense-in-depth wrapper used wherever an
 * author-supplied `bodyHtml` is rendered via `dangerouslySetInnerHTML`.
 * The wizard itself never renders that HTML directly — the bytes flow to
 * the backend and are echoed back inside the learner Reading player —
 * but having the helper available means future surfaces can opt in.
 */
describe('sanitizeBodyHtml', () => {
  it('strips <script> blocks', () => {
    const out = sanitizeBodyHtml('<p>Safe</p><script>alert("xss")</script>');
    expect(out).toBe('<p>Safe</p>');
  });

  it('strips event handlers like onclick=', () => {
    const out = sanitizeBodyHtml('<p onclick="alert(1)">Click</p>');
    expect(out).toBe('<p>Click</p>');
  });

  it('strips javascript: URLs in href', () => {
    const out = sanitizeBodyHtml('<a href="javascript:alert(1)">go</a>');
    expect(out).not.toContain('javascript:');
  });

  it('strips dangerous tags (iframe, object, embed, form, meta)', () => {
    const out = sanitizeBodyHtml('<p>ok</p><iframe src="x"></iframe><form><input/></form>');
    expect(out).not.toContain('<iframe');
    expect(out).not.toContain('<form');
  });

  it('strips non-image data: URLs', () => {
    const out = sanitizeBodyHtml('<a href="data:text/html;base64,PHNjcmlwdD4=">x</a>');
    expect(out).not.toContain('data:text');
  });

  it('preserves benign clinical markup', () => {
    const safe = '<p>Mr Smith presented with <strong>diabetes</strong>.</p><ul><li>HbA1c 8.0%</li></ul>';
    expect(sanitizeBodyHtml(safe)).toBe(safe);
  });

  it('handles null and undefined as empty string', () => {
    expect(sanitizeBodyHtml(null)).toBe('');
    expect(sanitizeBodyHtml(undefined)).toBe('');
  });

  it('handles empty string as empty string', () => {
    expect(sanitizeBodyHtml('')).toBe('');
  });

  it('handles malformed HTML without throwing', () => {
    expect(() => sanitizeBodyHtml('<p>unclosed')).not.toThrow();
    expect(() => sanitizeBodyHtml('<<>>')).not.toThrow();
  });

  it('regex fallback handles nested <script> blocks', () => {
    const out = regexFallback('<p>a</p><script>var x = "<script>";</script><p>b</p>');
    expect(out).not.toContain('<script>');
  });
});
