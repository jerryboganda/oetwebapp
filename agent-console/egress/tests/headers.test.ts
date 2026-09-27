import { describe, expect, it } from 'vitest';
import {
  parseAbsoluteHttpUrl,
  parseConnectAuthority,
  parseProxyAuthorization,
  stripHopByHop,
} from '../src/headers.js';

function basic(userPass: string): string {
  return `Basic ${Buffer.from(userPass, 'utf8').toString('base64')}`;
}

const SESSION = '01J9ZQ4Y8M3K2N7P5R6S8T0V1W';

describe('parseProxyAuthorization', () => {
  it('reads the session id from the Basic user part', () => {
    expect(parseProxyAuthorization(basic(`${SESSION}:x`))).toEqual({ sessionId: SESSION });
  });

  it('accepts a user without a password and a lower-case scheme', () => {
    expect(parseProxyAuthorization(`basic ${Buffer.from(SESSION).toString('base64')}`)).toEqual({ sessionId: SESSION });
  });

  it('treats a missing header as the system queue', () => {
    expect(parseProxyAuthorization(undefined)).toEqual({ sessionId: null });
  });

  it.each([
    ['Bearer abc', 'unsupported Proxy-Authorization scheme'],
    ['Basic', 'unsupported Proxy-Authorization scheme'],
    ['Basic !!!notbase64', 'unsupported Proxy-Authorization scheme'],
    [basic(':x'), 'invalid session id in Proxy-Authorization'],
    [basic('../../etc/passwd:x'), 'invalid session id in Proxy-Authorization'],
    [basic(`${'A'.repeat(65)}:x`), 'invalid session id in Proxy-Authorization'],
    [basic('sess ion:x'), 'invalid session id in Proxy-Authorization'],
  ])('ignores %s', (header, problem) => {
    expect(parseProxyAuthorization(header)).toEqual({ sessionId: null, problem });
  });

  it('refuses duplicated headers', () => {
    expect(parseProxyAuthorization([basic(`${SESSION}:x`), basic('other:x')]).sessionId).toBeNull();
  });
});

describe('parseConnectAuthority', () => {
  it.each([
    ['api.anthropic.com:443', { host: 'api.anthropic.com', port: 443 }],
    ['GitHub.com:22', { host: 'github.com', port: 22 }],
    ['[2606:4700:4700::1111]:443', { host: '2606:4700:4700::1111', port: 443 }],
    ['93.184.216.34:8443', { host: '93.184.216.34', port: 8443 }],
  ])('parses %s', (input, expected) => {
    expect(parseConnectAuthority(input)).toEqual(expected);
  });

  it.each([
    ['github.com'],
    ['github.com:'],
    ['github.com:0'],
    ['github.com:65536'],
    ['github.com:443abc'],
    [':443'],
    ['2606:4700::1111:443'],
    ['[2606:4700::1111]443'],
    ['localhost:443'],
    ['http://github.com:443'],
  ])('rejects %s', (input) => {
    expect(parseConnectAuthority(input)).toBeNull();
  });
});

describe('parseAbsoluteHttpUrl', () => {
  it('parses host, default port, path and query', () => {
    expect(parseAbsoluteHttpUrl('http://GitHub.com/a/b?c=1')).toEqual({
      host: 'github.com',
      port: 80,
      path: '/a/b?c=1',
      hostHeader: 'github.com',
    });
  });

  it('keeps an explicit port in the Host header', () => {
    expect(parseAbsoluteHttpUrl('http://example.com:8080/')).toEqual({
      host: 'example.com',
      port: 8080,
      path: '/',
      hostHeader: 'example.com:8080',
    });
  });

  it('normalizes numeric IPv4 spellings so the private-range check sees them', () => {
    expect(parseAbsoluteHttpUrl('http://2130706433/')?.host).toBe('127.0.0.1');
  });

  it.each([
    ['https://github.com/'],
    ['http://user:pass@github.com/'],
    ['ftp://github.com/'],
    ['/relative/path'],
    ['http://localhost/'],
  ])('rejects %s', (input) => {
    expect(parseAbsoluteHttpUrl(input)).toBeNull();
  });
});

describe('stripHopByHop', () => {
  it('removes hop-by-hop, proxy credentials, control token and Connection-listed headers', () => {
    const out = stripHopByHop({
      host: 'github.com',
      connection: 'keep-alive, X-Custom-Hop',
      'keep-alive': 'timeout=5',
      'proxy-authorization': 'Basic abc',
      'proxy-connection': 'keep-alive',
      'x-oet-control-token': 'secret-value-that-must-not-leak',
      'x-custom-hop': '1',
      'transfer-encoding': 'chunked',
      upgrade: 'websocket',
      te: 'trailers',
      accept: 'application/json',
      'user-agent': 'git/2.45',
    });
    expect(out).toEqual({ host: 'github.com', accept: 'application/json', 'user-agent': 'git/2.45' });
  });
});
