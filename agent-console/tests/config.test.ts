import { describe, expect, it } from 'vitest';
import { ConfigError, MIN_TOKEN_LENGTH, loadConfig, parseOwnerAccountIds } from '../src/config.js';

const good = 'g'.repeat(MIN_TOKEN_LENGTH);
const baseEnv = { OWNER_AGENT_OWNER_ACCOUNT_IDS: 'owner-1' };
const noFiles = (): string | null => null;

describe('startup token requirements', () => {
  it('refuses to start without an internal token', () => {
    expect(() => loadConfig({ env: baseEnv, readFile: noFiles })).toThrow(ConfigError);
    expect(() => loadConfig({ env: baseEnv, readFile: noFiles })).toThrow(/Internal token missing/);
  });

  it('refuses a token shorter than 32 characters (file or env)', () => {
    expect(() => loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: 'short' }, readFile: noFiles })).toThrow(/at least 32/);
    expect(() => loadConfig({ env: baseEnv, readFile: () => 'x'.repeat(31) })).toThrow(/at least 32/);
  });

  it('reads the token file first (default /run/secrets path), then the env fallback', () => {
    const seen: string[] = [];
    const fromFile = loadConfig({
      env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: 'e'.repeat(40) },
      readFile: (file) => {
        seen.push(file);
        return file === '/run/secrets/owner_agent_internal_token' ? `${good}\n` : null;
      },
    });
    expect(fromFile.internalToken).toBe(good);
    expect(seen).toContain('/run/secrets/owner_agent_internal_token');

    const fromEnv = loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: 'e'.repeat(40) }, readFile: noFiles });
    expect(fromEnv.internalToken).toBe('e'.repeat(40));

    const customFile = loadConfig({
      env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN_FILE: '/custom/token' },
      readFile: (file) => (file === '/custom/token' ? good : null),
    });
    expect(customFile.internalToken).toBe(good);
  });

  it('requires at least one owner account id', () => {
    expect(() => loadConfig({ env: { OWNER_AGENT_INTERNAL_TOKEN: good }, readFile: noFiles })).toThrow(/OWNER_AGENT_OWNER_ACCOUNT_IDS/);
  });

  it('validates the proxy token when present and keeps it distinct', () => {
    expect(() => loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: good, OWNER_AGENT_PROXY_TOKEN: 'short' }, readFile: noFiles })).toThrow(/Proxy token/);
    expect(() => loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: good, OWNER_AGENT_PROXY_TOKEN: good }, readFile: noFiles })).toThrow(/differ/);
    const ok = loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: good }, readFile: noFiles });
    expect(ok.proxyToken).toBeNull();
  });

  it('applies production defaults from the contract', () => {
    const config = loadConfig({ env: { ...baseEnv, OWNER_AGENT_INTERNAL_TOKEN: good }, readFile: noFiles });
    expect(config.port).toBe(8410);
    expect(config.maxConcurrentTurns).toBe(2);
    expect(config.idleCloseMs).toBe(10 * 60_000);
    expect(config.approvalTtlMs).toBe(30 * 60_000);
    expect(config.leaseMaxMs).toBe(3 * 60_000);
    expect(config.retentionDays).toBe(90);
    expect(config.diffCapBytes).toBe(2 * 1024 * 1024);
    expect(config.dockerHost).toBe('tcp://oet-agent-dockerproxy:2375');
    expect(config.snapshot.fullDumpMinIntervalMs).toBe(10 * 60_000);
    expect(config.ship.publicWindowMaxMs).toBe(90 * 60_000);
    expect(config.ship.healthUrls).toHaveLength(3);
    expect(config.updatePendingFile).toBeNull();
  });
});

describe('owner account allow-list', () => {
  it('parses comma lists case-insensitively', () => {
    expect([...parseOwnerAccountIds(' A-1, b-2 ;C-3 ')]).toEqual(['a-1', 'b-2', 'c-3']);
    expect(parseOwnerAccountIds(undefined).size).toBe(0);
  });
});
