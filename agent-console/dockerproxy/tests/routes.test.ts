import { describe, expect, it } from 'vitest';
import { buildForwardPath, parseDockerPath, type DockerRoute } from '../src/routes.js';

function route(method: string, url: string): DockerRoute | string {
  const parsed = parseDockerPath(method, url);
  return parsed.ok ? parsed.value.route : `error: ${parsed.error}`;
}

describe('parseDockerPath', () => {
  it.each<[string, string, DockerRoute]>([
    ['GET', '/_ping', { kind: 'ping' }],
    ['HEAD', '/_ping', { kind: 'ping' }],
    ['GET', '/v1.47/version', { kind: 'version' }],
    ['GET', '/info', { kind: 'info' }],
    ['GET', '/v1.45/events?since=0', { kind: 'events' }],
    ['GET', '/v1.47/containers/json?all=1', { kind: 'containers-list' }],
    ['POST', '/v1.47/containers/create?name=x', { kind: 'containers-create' }],
    ['POST', '/containers/prune', { kind: 'containers-prune' }],
    ['GET', '/containers/oet-api-blue/json', { kind: 'container', id: 'oet-api-blue', action: 'json' }],
    ['DELETE', '/containers/oet-web-blue?force=1', { kind: 'container', id: 'oet-web-blue', action: null }],
    ['GET', '/containers/oet-api-blue/attach/ws', { kind: 'container', id: 'oet-api-blue', action: 'attach/ws' }],
    ['POST', '/v1.47/exec/0123abcd/start', { kind: 'exec', id: '0123abcd', action: 'start' }],
    ['GET', '/images/json', { kind: 'images-list' }],
    ['POST', '/images/create?fromImage=alpine', { kind: 'images-collection', action: 'create' }],
    ['GET', '/images/ghcr.io/jerryboganda/oetwebapp-api:abc/json', { kind: 'image', name: 'ghcr.io/jerryboganda/oetwebapp-api:abc', action: 'json' }],
    ['DELETE', '/images/ghcr.io/jerryboganda/oetwebapp-api:abc', { kind: 'image', name: 'ghcr.io/jerryboganda/oetwebapp-api:abc', action: null }],
    ['POST', '/images/pgvector/pgvector:pg17/push', { kind: 'image', name: 'pgvector/pgvector:pg17', action: 'push' }],
    ['GET', '/networks', { kind: 'networks-list' }],
    ['POST', '/networks/create', { kind: 'networks-create' }],
    ['GET', '/networks/oetwebsite_internal', { kind: 'network', id: 'oetwebsite_internal', action: null }],
    ['POST', '/networks/oetwebsite_internal/connect', { kind: 'network', id: 'oetwebsite_internal', action: 'connect' }],
    ['GET', '/volumes', { kind: 'volumes-list' }],
    ['POST', '/volumes/prune', { kind: 'volumes-prune' }],
    ['GET', '/volumes/oetwebsite_oet_postgres_data', { kind: 'volume', name: 'oetwebsite_oet_postgres_data' }],
    ['POST', '/build/prune', { kind: 'build-prune' }],
    ['GET', '/system/df', { kind: 'system-df' }],
    ['POST', '/build', { kind: 'unsupported' }],
    ['POST', '/commit?container=x', { kind: 'unsupported' }],
    ['GET', '/swarm', { kind: 'unsupported' }],
    ['GET', '/plugins', { kind: 'unsupported' }],
  ])('%s %s', (method, url, expected) => {
    expect(route(method, url)).toEqual(expected);
  });

  it('decodes percent-encoding before classifying', () => {
    expect(route('GET', '/containers/oet%2Dagent%2Dconsole/json')).toEqual({ kind: 'container', id: 'oet-agent-console', action: 'json' });
  });

  it.each([
    ['relative', 'containers/json'],
    ['encoded slash', '/containers/..%2F..%2Fetc/json'],
    ['encoded backslash', '/containers/a%5Cb/json'],
    ['empty segment', '/containers//json'],
    ['trailing slash', '/containers/json/'],
    ['dot segment', '/containers/./json'],
    ['dot-dot segment', '/containers/../json'],
    ['bad percent-encoding', '/containers/%zz/json'],
    ['space', '/containers/a%20b/json'],
    ['version only', '/v1.47'],
  ])('rejects %s', (_label, url) => {
    expect(parseDockerPath('GET', url).ok).toBe(false);
  });

  it('keeps the query string for forwarding', () => {
    const parsed = parseDockerPath('GET', '/v1.47/containers/json?all=1&filters=%7B%7D');
    expect(parsed.ok && parsed.value.search).toBe('?all=1&filters=%7B%7D');
    expect(parsed.ok && parsed.value.query.get('filters')).toBe('{}');
  });
});

describe('buildForwardPath', () => {
  it('replaces the caller reference with the resolved full id', () => {
    const parsed = parseDockerPath('POST', '/v1.47/containers/oet-api-blue/stop?t=10');
    if (!parsed.ok) throw new Error('parse failed');
    expect(buildForwardPath(parsed.value, 'a'.repeat(64))).toBe(`/v1.47/containers/${'a'.repeat(64)}/stop?t=10`);
  });

  it('ignores a non-hex id and leaves images untouched', () => {
    const container = parseDockerPath('GET', '/containers/oet-api-blue/json');
    const image = parseDockerPath('GET', '/images/ghcr.io/o/oetwebapp-api:1/json');
    if (!container.ok || !image.ok) throw new Error('parse failed');
    expect(buildForwardPath(container.value, 'not-an-id')).toBe('/containers/oet-api-blue/json');
    expect(buildForwardPath(image.value, 'a'.repeat(64))).toBe('/images/ghcr.io/o/oetwebapp-api:1/json');
  });
});
