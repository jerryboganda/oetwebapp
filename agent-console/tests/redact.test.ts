import { describe, expect, it } from 'vitest';
import { Redactor } from '../src/redact.js';

// Secret-shaped values are assembled at runtime so no literal in this file
// matches a scanner rule.
const fake = {
  githubPat: ['github', 'pat', '11AAAAAAA0', 'b'.repeat(59)].join('_'),
  ghs: `${'gh'}s_${'C'.repeat(36)}`,
  gho: `${'gh'}o_${'D'.repeat(36)}`,
  ghp: `${'gh'}p_${'E'.repeat(36)}`,
  sk: `${'s'}k-proj-${'f'.repeat(32)}`,
  skAnt: `${'s'}k-ant-api03-${'g'.repeat(40)}`,
  slack: `${'xo'}xb-123456789012-${'h'.repeat(24)}`,
  jwt: `${'ey'}JhbGciOiJIUzI1NiJ9.${'ey'}JzdWIiOiIxMjM0In0.${'s'.repeat(20)}`,
  pgPassword: `pw${'9'.repeat(12)}`,
  bearer: 'i'.repeat(40),
};

describe('Redactor', () => {
  it('replaces known secret values anywhere in text', () => {
    const secret = `known-${'k'.repeat(30)}`;
    const r = new Redactor([secret]);
    expect(r.redact(`token=${secret}; again ${secret}`)).toBe('token=[REDACTED:secret]; again [REDACTED:secret]');
  });

  it('ignores very short known values (would mangle ordinary text)', () => {
    const r = new Redactor(['abc']);
    expect(r.redact('abc abc')).toBe('abc abc');
  });

  it('also registers the password inside a known database URL', () => {
    const url = `postgres://oet_owner_agent:${fake.pgPassword}@oet-agent-dbproxy:5432/oet`;
    const r = new Redactor([url]);
    expect(r.redact(`PGPASSWORD=${fake.pgPassword}`)).toBe('PGPASSWORD=[REDACTED:secret]');
  });

  it.each([
    ['JWT', fake.jwt, '[REDACTED:jwt]'],
    ['github_pat_', fake.githubPat, '[REDACTED:github_pat]'],
    ['ghs_', fake.ghs, '[REDACTED:github_token]'],
    ['gho_', fake.gho, '[REDACTED:github_token]'],
    ['ghp_', fake.ghp, '[REDACTED:github_token]'],
    ['sk- (OpenAI)', fake.sk, '[REDACTED:api_key]'],
    ['sk- (Anthropic)', fake.skAnt, '[REDACTED:api_key]'],
    ['xox*', fake.slack, '[REDACTED:slack_token]'],
  ])('redacts %s patterns', (_label, secret, replacement) => {
    const r = new Redactor();
    const out = r.redact(`value: ${secret} end`);
    expect(out).toBe(`value: ${replacement} end`);
    expect(out).not.toContain(secret);
  });

  it('redacts the password of postgres:// URLs but keeps host and user', () => {
    const r = new Redactor();
    const out = r.redact(`connecting to postgres://app_user:${fake.pgPassword}@oet-postgres:5432/oet`);
    expect(out).toBe('connecting to postgres://app_user:[REDACTED:password]@oet-postgres:5432/oet');
  });

  it('redacts Bearer tokens', () => {
    const r = new Redactor();
    expect(r.redact(`Authorization: Bearer ${fake.bearer}`)).toBe('Authorization: Bearer [REDACTED:bearer]');
  });

  it('does not touch ordinary words that merely contain sk-', () => {
    const r = new Redactor();
    const text = 'run the task-scheduler-for-everything-now and desk-top';
    expect(r.redact(text)).toBe(text);
  });

  it('redacts nested structures and sensitive keys without mutating the input', () => {
    const r = new Redactor();
    const input = {
      command: `curl -H "Authorization: Bearer ${fake.bearer}" https://api.github.com`,
      nested: [{ token: 'plain-value-123', note: fake.ghs }],
      count: 3,
      ok: true,
      nothing: null,
    };
    const out = r.redactDeep(input);
    expect(out.command).toContain('[REDACTED:bearer]');
    expect(out.nested[0]?.token).toBe('[REDACTED:field]');
    expect(out.nested[0]?.note).toBe('[REDACTED:github_token]');
    expect(out.count).toBe(3);
    expect(out.ok).toBe(true);
    expect(out.nothing).toBeNull();
    expect(input.nested[0]?.token).toBe('plain-value-123');
  });
});
