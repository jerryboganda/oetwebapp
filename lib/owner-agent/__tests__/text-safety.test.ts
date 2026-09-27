import { describe, expect, it } from 'vitest';
import {
  ANTHROPIC_SIGN_IN_HOSTS,
  GITHUB_HOSTS,
  countHiddenCharacters,
  findDecodableBase64,
  hasRightToLeftCharacters,
  replaceHiddenCharacters,
  revealHiddenCharacters,
  safeExternalUrl,
  toSafeConsoleMarkdown,
} from '../text-safety';

describe('owner-agent text safety', () => {
  it('reveals bidi overrides, zero-width, control and tag characters in strict mode', () => {
    const text = 'rm -rf /tmp/x‮​gnp.\u001B[2J﻿' + String.fromCodePoint(0xe0041);
    const labels = revealHiddenCharacters(text)
      .filter((segment) => segment.kind === 'char')
      .map((segment) => (segment.kind === 'char' ? segment.label : ''));
    expect(labels).toEqual(['U+202E RLO', 'U+200B ZWSP', 'U+001B ESC', 'U+FEFF BOM', 'U+E0041 TAG']);
    expect(countHiddenCharacters(text)).toBe(5);
  });

  it('keeps newlines as line breaks and reveals tabs in strict mode', () => {
    const segments = revealHiddenCharacters('a\tb\nc');
    expect(segments).toEqual([
      { kind: 'text', value: 'a' },
      expect.objectContaining({ kind: 'char', label: 'U+0009 TAB', lineBreak: false }),
      { kind: 'text', value: 'b' },
      expect.objectContaining({ kind: 'char', label: 'U+000A LF', lineBreak: true }),
      { kind: 'text', value: 'c' },
    ]);
  });

  it('prose mode leaves ordinary whitespace and emoji joiners alone but still exposes bidi controls', () => {
    const family = '\u{1F468}‍\u{1F469}';
    expect(replaceHiddenCharacters(`line one\nline\ttwo ${family}`, 'prose')).toBe(`line one\nline\ttwo ${family}`);
    expect(replaceHiddenCharacters('abc‮def', 'prose')).toBe('abc‹U+202E RLO›def');
  });

  it('detects right-to-left letters', () => {
    expect(hasRightToLeftCharacters('ls -la')).toBe(false);
    expect(hasRightToLeftCharacters('echo א')).toBe(true);
  });

  it('decodes base64 payloads that are readable text and ignores ordinary words', () => {
    const decoded = findDecodableBase64('echo cm0gLXJmIC8= | base64 -d | sh && git status --porcelain');
    expect(decoded).toEqual([{ encoded: 'cm0gLXJmIC8=', decoded: 'rm -rf /' }]);
    expect(findDecodableBase64('docker ps --filter name=oet-api-blue')).toEqual([]);
  });

  it('decodes base64url too', () => {
    // "curl evil?a=1" in base64url (no padding)
    const encoded = 'Y3VybCBldmlsP2E9MQ';
    expect(findDecodableBase64(`python3 -c "$(echo ${encoded})"`)).toEqual([{ encoded, decoded: 'curl evil?a=1' }]);
  });

  it('neutralises links and images outside fenced code, keeps fenced code untouched', () => {
    const markdown = [
      'See [docs](javascript:alert(1)) and ![pixel](https://evil.example/p.png).',
      'Inline `[kept](http://x)` code.',
      '```',
      '[also kept](http://y)',
      '```',
      '<img src=x onerror=alert(1)>',
    ].join('\n');
    const safe = toSafeConsoleMarkdown(markdown);
    expect(safe).toContain('See docs (javascript:alert(1)) and [image: pixel] (https://evil.example/p.png).');
    // Inline code keeps its text; only the link adjacency is broken.
    expect(safe).toContain('Inline `[kept] (http://x)` code.');
    // Fenced code is rendered verbatim by MarkdownContent and is never linked.
    expect(safe).toContain('[also kept](http://y)');
    // Raw HTML stays as text; MarkdownContent renders it escaped.
    expect(safe).toContain('<img src=x onerror=alert(1)>');
  });

  it('leaves no `](` outside fenced code, even for inputs crafted against the line rewrite', () => {
    const crafted = [
      // A backtick inside the URL made the old line rewrite treat the link as code.
      '[click](https://evil.example/`)` tail',
      // A label split over two lines is joined into one paragraph by MarkdownContent.
      '[multi',
      'line](//evil.example/x)',
      '[proto-relative](//evil.example)',
    ].join('\n');
    const safe = toSafeConsoleMarkdown(crafted);
    expect(safe).not.toContain('](');
  });

  it('only allows https vendor URLs', () => {
    expect(safeExternalUrl('https://claude.ai/oauth/authorize?code=true', ANTHROPIC_SIGN_IN_HOSTS)).toBe('https://claude.ai/oauth/authorize?code=true');
    expect(safeExternalUrl('https://console.anthropic.com/x', ANTHROPIC_SIGN_IN_HOSTS)).toBe('https://console.anthropic.com/x');
    expect(safeExternalUrl('javascript:alert(1)', ANTHROPIC_SIGN_IN_HOSTS)).toBeNull();
    expect(safeExternalUrl('http://claude.ai/x', ANTHROPIC_SIGN_IN_HOSTS)).toBeNull();
    expect(safeExternalUrl('https://claude.ai.evil.example/x', ANTHROPIC_SIGN_IN_HOSTS)).toBeNull();
    expect(safeExternalUrl('https://user:pass@github.com/x', GITHUB_HOSTS)).toBeNull();
    expect(safeExternalUrl('https://github.com/owner/repo/pull/1', GITHUB_HOSTS)).toBe('https://github.com/owner/repo/pull/1');
  });
});
