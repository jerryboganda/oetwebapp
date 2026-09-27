import { describe, expect, it } from 'vitest';
import {
  STATIC_ALLOWLIST,
  evaluateEgress,
  isForbiddenAddress,
  matchesAllowlist,
  normalizeHost,
  type EgressKind,
  type EgressPolicy,
} from '../src/policy.js';

function policy(grants: string[] = []): EgressPolicy {
  const set = new Set(grants);
  return {
    allowlist: STATIC_ALLOWLIST,
    connectPorts: new Set([443]),
    httpPorts: new Set([80]),
    hasGrant: (sessionId, host, port) => set.has(`${sessionId}|${host}:${port}`),
  };
}

describe('static allowlist (CONTRACT.md §6)', () => {
  it('contains exactly the contract hosts', () => {
    expect([...STATIC_ALLOWLIST].sort()).toEqual(
      [
        'api.anthropic.com',
        'claude.ai',
        'console.anthropic.com',
        'platform.claude.com',
        'statsig.anthropic.com',
        'chatgpt.com',
        'auth.openai.com',
        'api.openai.com',
        'ab.chatgpt.com',
        'github.com',
        'api.github.com',
        'githubusercontent.com',
        'ghcr.io',
        'registry.npmjs.org',
        'oetwithdrhesham.co.uk',
      ].sort(),
    );
  });

  it.each([
    ['api.anthropic.com', true],
    ['claude.ai', true],
    ['www.claude.ai', true],
    ['console.anthropic.com', true],
    ['platform.claude.com', true],
    ['statsig.anthropic.com', true],
    ['anthropic.com', false],
    ['evil-anthropic.com', false],
    ['api.anthropic.com.evil.io', false],
    ['chatgpt.com', true],
    ['ab.chatgpt.com', true],
    ['auth.openai.com', true],
    ['api.openai.com', true],
    ['openai.com', false],
    ['files.openai.com', false],
    ['github.com', true],
    ['codeload.github.com', true],
    ['api.github.com', true],
    ['evilgithub.com', false],
    ['raw.githubusercontent.com', true],
    ['objects.githubusercontent.com', true],
    ['githubusercontent.com.evil.io', false],
    ['ghcr.io', true],
    ['pkg-containers.ghcr.io', true],
    ['registry.npmjs.org', true],
    ['npmjs.org', false],
    ['registry.npmjs.org.evil.io', false],
    ['oetwithdrhesham.co.uk', true],
    ['app.oetwithdrhesham.co.uk', true],
    ['api.oetwithdrhesham.co.uk', true],
    ['xoetwithdrhesham.co.uk', false],
    ['example.com', false],
    ['140.82.112.3', false],
  ])('matchesAllowlist(%s) === %s', (host, expected) => {
    expect(matchesAllowlist(host)).toBe(expected);
  });
});

describe('normalizeHost', () => {
  it.each([
    ['API.Anthropic.COM', 'api.anthropic.com'],
    ['api.anthropic.com.', 'api.anthropic.com'],
    ['  github.com  ', 'github.com'],
    ['[::1]', '::1'],
    ['127.0.0.1', '127.0.0.1'],
    ['münchen.de', 'xn--mnchen-3ya.de'],
  ])('normalizes %s → %s', (input, expected) => {
    expect(normalizeHost(input)).toBe(expected);
  });

  it.each([
    [''],
    ['localhost'],
    ['oet-agent-console'],
    ['2130706433'],
    ['0177.0.0.1'],
    ['0x7f.0x1'],
    ['exa mple.com'],
    ['under_score.example.com'],
    ['-leading.example.com'],
    ['a'.repeat(64) + '.com'],
    ['bad..dots.com'],
  ])('rejects %s', (input) => {
    expect(normalizeHost(input)).toBeNull();
  });
});

describe('isForbiddenAddress', () => {
  it.each([
    ['127.0.0.1', true],
    ['10.1.2.3', true],
    ['172.18.0.5', true],
    ['192.168.1.1', true],
    ['169.254.169.254', true],
    ['100.64.0.1', true],
    ['0.0.0.0', true],
    ['224.0.0.1', true],
    ['255.255.255.255', true],
    ['::1', true],
    ['::', true],
    ['::ffff:127.0.0.1', true],
    ['::ffff:7f00:1', true],
    ['::ffff:10.0.0.1', true],
    ['fd00::1', true],
    ['fe80::1', true],
    ['fe80::1%eth0', true],
    ['2002:7f00:1::', true],
    ['not-an-ip', true],
    ['8.8.8.8', false],
    ['140.82.112.3', false],
    ['::ffff:8.8.8.8', false],
    ['2606:4700:4700::1111', false],
  ])('isForbiddenAddress(%s) === %s', (address, expected) => {
    expect(isForbiddenAddress(address)).toBe(expected);
  });
});

interface Row {
  name: string;
  host: string;
  port: number;
  kind: EgressKind;
  sessionId: string | null;
  control?: boolean;
  grants?: string[];
  expected: 'allow' | 'deny' | 'approval';
  via?: 'static' | 'session' | 'control';
}

const SESSION = '01J9ZQ4Y8M3K2N7P5R6S8T0V1W';
const OTHER = '01J9ZQ4Y8M3K2N7P5R6S8T0V1X';

const rows: Row[] = [
  { name: 'allowlisted CONNECT 443', host: 'api.anthropic.com', port: 443, kind: 'connect', sessionId: SESSION, expected: 'allow', via: 'static' },
  { name: 'allowlisted CONNECT without session', host: 'github.com', port: 443, kind: 'connect', sessionId: null, expected: 'allow', via: 'static' },
  { name: 'allowlisted plain HTTP 80', host: 'github.com', port: 80, kind: 'http', sessionId: SESSION, expected: 'allow', via: 'static' },
  { name: 'allowlisted host on ssh port', host: 'github.com', port: 22, kind: 'connect', sessionId: SESSION, expected: 'approval' },
  { name: 'allowlisted host HTTP on 8080', host: 'github.com', port: 8080, kind: 'http', sessionId: SESSION, expected: 'approval' },
  { name: 'unknown host', host: 'example.com', port: 443, kind: 'connect', sessionId: SESSION, expected: 'approval' },
  { name: 'unknown host without session', host: 'example.com', port: 443, kind: 'connect', sessionId: null, expected: 'approval' },
  { name: 'public IP literal', host: '93.184.216.34', port: 443, kind: 'connect', sessionId: SESSION, expected: 'approval' },
  { name: 'loopback literal', host: '127.0.0.1', port: 443, kind: 'connect', sessionId: SESSION, expected: 'deny' },
  { name: 'metadata endpoint', host: '169.254.169.254', port: 80, kind: 'http', sessionId: SESSION, expected: 'deny' },
  { name: 'docker bridge address', host: '172.17.0.1', port: 2375, kind: 'connect', sessionId: SESSION, expected: 'deny' },
  { name: 'mapped loopback v6', host: '::ffff:127.0.0.1', port: 443, kind: 'connect', sessionId: SESSION, expected: 'deny' },
  { name: 'invalid port', host: 'github.com', port: 0, kind: 'connect', sessionId: SESSION, expected: 'deny' },
  { name: 'session grant', host: 'example.com', port: 443, kind: 'connect', sessionId: SESSION, grants: [`${SESSION}|example.com:443`], expected: 'allow', via: 'session' },
  { name: 'grant is per session', host: 'example.com', port: 443, kind: 'connect', sessionId: OTHER, grants: [`${SESSION}|example.com:443`], expected: 'approval' },
  { name: 'grant is per port', host: 'example.com', port: 8443, kind: 'connect', sessionId: SESSION, grants: [`${SESSION}|example.com:443`], expected: 'approval' },
  { name: 'control plane bypass', host: 'example.com', port: 443, kind: 'connect', sessionId: null, control: true, expected: 'allow', via: 'control' },
  { name: 'control plane cannot reach private ranges', host: '10.0.0.8', port: 443, kind: 'connect', sessionId: null, control: true, expected: 'deny' },
];

describe('evaluateEgress policy table', () => {
  it.each(rows)('$name', (row) => {
    const verdict = evaluateEgress(
      { host: row.host, port: row.port, kind: row.kind, sessionId: row.sessionId, control: row.control ?? false },
      policy(row.grants),
    );
    expect(verdict.decision).toBe(row.expected);
    if (verdict.decision === 'allow') expect(verdict.via).toBe(row.via);
    if (verdict.decision !== 'allow') expect(verdict.reasons.length).toBeGreaterThan(0);
  });

  it('explains every approval reason', () => {
    const verdict = evaluateEgress({ host: 'example.com', port: 8080, kind: 'http', sessionId: null, control: false }, policy());
    expect(verdict.decision).toBe('approval');
    expect(verdict.reasons).toEqual([
      'example.com is not on the static egress allowlist',
      'non-standard port 8080 for plain HTTP',
      'unencrypted plain-HTTP request',
      'no agent session attribution (system queue)',
    ]);
  });
});
