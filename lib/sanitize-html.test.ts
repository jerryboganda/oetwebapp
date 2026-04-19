import { sanitizeRichHtml } from './sanitize-html';

describe('sanitizeRichHtml', () => {
  it('removes scripts and inline handlers', () => {
    const html = '<p onclick="alert(1)">Hello <script>alert(1)</script><a href="javascript:evil()">link</a></p>';

    const sanitized = sanitizeRichHtml(html);

    expect(sanitized).not.toContain('<script>');
    expect(sanitized).not.toContain('onclick');
    expect(sanitized).not.toContain('javascript:evil');
  });
});
