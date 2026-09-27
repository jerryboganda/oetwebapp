// Docker Engine API path classifier. Pure.
//
// The daemon's router matches the percent-DECODED path, so the proxy decodes,
// validates each segment against a strict character set, classifies it, and
// later forwards a path rebuilt from these validated segments — the policy and
// the daemon can never disagree about which object a request names.

export type DockerRoute =
  | { kind: 'ping' }
  | { kind: 'version' }
  | { kind: 'info' }
  | { kind: 'events' }
  | { kind: 'system-df' }
  | { kind: 'system-prune' }
  | { kind: 'build-prune' }
  | { kind: 'containers-list' }
  | { kind: 'containers-create' }
  | { kind: 'containers-prune' }
  | { kind: 'container'; id: string; action: string | null }
  | { kind: 'exec'; id: string; action: string }
  | { kind: 'images-list' }
  | { kind: 'images-prune' }
  | { kind: 'images-collection'; action: string }
  | { kind: 'image'; name: string; action: string | null }
  | { kind: 'networks-list' }
  | { kind: 'networks-create' }
  | { kind: 'networks-prune' }
  | { kind: 'network'; id: string; action: string | null }
  | { kind: 'volumes-list' }
  | { kind: 'volumes-create' }
  | { kind: 'volumes-prune' }
  | { kind: 'volume'; name: string }
  | { kind: 'unsupported' };

export interface ParsedPath {
  /** "/v1.47" or "" */
  versionPrefix: string;
  /** Decoded, validated path segments without the version prefix. */
  segments: string[];
  route: DockerRoute;
  query: URLSearchParams;
  /** Original query string including the leading "?" (or ""). */
  search: string;
}

export type ParseResult = { ok: true; value: ParsedPath } | { ok: false; error: string };

/** Container / network / volume names and ids (docker: [a-zA-Z0-9][a-zA-Z0-9_.-]+). */
export const NAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9_.-]{0,254}$/;
const SEGMENT_PATTERN = /^[A-Za-z0-9._:@-]{1,255}$/;
const EXEC_ID_PATTERN = /^[A-Za-z0-9]{1,128}$/;
const VERSION_PATTERN = /^v[0-9]{1,2}\.[0-9]{1,3}$/;
const UNSUPPORTED: DockerRoute = { kind: 'unsupported' };

const IMAGE_COLLECTION = new Set(['create', 'load', 'search', 'get']);
const IMAGE_ACTIONS = new Set(['json', 'history', 'push', 'tag', 'get']);

function classifyContainers(method: string, rest: string[]): DockerRoute {
  const [first, second, third] = rest;
  if (first === undefined) return UNSUPPORTED;
  if (rest.length === 1) {
    if (first === 'json' && method !== 'DELETE') return { kind: 'containers-list' };
    if (first === 'create' && method === 'POST') return { kind: 'containers-create' };
    if (first === 'prune' && method === 'POST') return { kind: 'containers-prune' };
    return NAME_PATTERN.test(first) ? { kind: 'container', id: first, action: null } : UNSUPPORTED;
  }
  if (!NAME_PATTERN.test(first) || second === undefined) return UNSUPPORTED;
  if (rest.length === 2) return { kind: 'container', id: first, action: second };
  if (rest.length === 3 && second === 'attach' && third === 'ws') return { kind: 'container', id: first, action: 'attach/ws' };
  return UNSUPPORTED;
}

function classifyImages(method: string, rest: string[]): DockerRoute {
  const [first] = rest;
  if (first === undefined) return UNSUPPORTED;
  if (rest.length === 1) {
    if (first === 'json' && method !== 'DELETE') return { kind: 'images-list' };
    if (first === 'prune' && method === 'POST') return { kind: 'images-prune' };
    if (IMAGE_COLLECTION.has(first) && method !== 'DELETE') return { kind: 'images-collection', action: first };
  }
  if (method === 'DELETE') return { kind: 'image', name: rest.join('/'), action: null };
  const action = rest[rest.length - 1];
  if (rest.length >= 2 && action !== undefined && IMAGE_ACTIONS.has(action)) {
    return { kind: 'image', name: rest.slice(0, -1).join('/'), action };
  }
  return UNSUPPORTED;
}

function classifyNetworks(method: string, rest: string[]): DockerRoute {
  const [first, second] = rest;
  if (first === undefined) return method === 'GET' || method === 'HEAD' ? { kind: 'networks-list' } : UNSUPPORTED;
  if (rest.length === 1) {
    if (first === 'create' && method === 'POST') return { kind: 'networks-create' };
    if (first === 'prune' && method === 'POST') return { kind: 'networks-prune' };
    return NAME_PATTERN.test(first) ? { kind: 'network', id: first, action: null } : UNSUPPORTED;
  }
  if (rest.length === 2 && second !== undefined && NAME_PATTERN.test(first)) return { kind: 'network', id: first, action: second };
  return UNSUPPORTED;
}

function classifyVolumes(method: string, rest: string[]): DockerRoute {
  const [first] = rest;
  if (first === undefined) return method === 'GET' || method === 'HEAD' ? { kind: 'volumes-list' } : UNSUPPORTED;
  if (rest.length !== 1) return UNSUPPORTED;
  if (first === 'create' && method === 'POST') return { kind: 'volumes-create' };
  if (first === 'prune' && method === 'POST') return { kind: 'volumes-prune' };
  return NAME_PATTERN.test(first) ? { kind: 'volume', name: first } : UNSUPPORTED;
}

function single(rest: string[], route: DockerRoute): DockerRoute {
  return rest.length === 0 ? route : UNSUPPORTED;
}

export function classify(method: string, segments: string[]): DockerRoute {
  const [head, ...rest] = segments;
  switch (head) {
    case '_ping':
      return single(rest, { kind: 'ping' });
    case 'version':
      return single(rest, { kind: 'version' });
    case 'info':
      return single(rest, { kind: 'info' });
    case 'events':
      return single(rest, { kind: 'events' });
    case 'system':
      if (rest.length === 1 && rest[0] === 'df') return { kind: 'system-df' };
      if (rest.length === 1 && rest[0] === 'prune') return { kind: 'system-prune' };
      return UNSUPPORTED;
    case 'build':
      return rest.length === 1 && rest[0] === 'prune' ? { kind: 'build-prune' } : UNSUPPORTED;
    case 'containers':
      return classifyContainers(method, rest);
    case 'exec': {
      const [id, action] = rest;
      return rest.length === 2 && id !== undefined && action !== undefined && EXEC_ID_PATTERN.test(id)
        ? { kind: 'exec', id, action }
        : UNSUPPORTED;
    }
    case 'images':
      return classifyImages(method, rest);
    case 'networks':
      return classifyNetworks(method, rest);
    case 'volumes':
      return classifyVolumes(method, rest);
    default:
      return UNSUPPORTED;
  }
}

export function parseDockerPath(method: string, rawUrl: string): ParseResult {
  if (!rawUrl.startsWith('/')) return { ok: false, error: 'request target must be origin-form' };
  const q = rawUrl.indexOf('?');
  const rawPath = q >= 0 ? rawUrl.slice(0, q) : rawUrl;
  const search = q >= 0 ? rawUrl.slice(q) : '';
  if (rawPath.length > 2048) return { ok: false, error: 'path too long' };
  if (/%2f|%5c|%00/i.test(rawPath) || rawPath.includes('\\')) {
    return { ok: false, error: 'encoded path separators are not allowed' };
  }
  const segments: string[] = [];
  for (const raw of rawPath.split('/').slice(1)) {
    let decoded: string;
    try {
      decoded = decodeURIComponent(raw);
    } catch {
      return { ok: false, error: 'invalid percent-encoding' };
    }
    if (decoded === '' || decoded === '.' || decoded === '..') {
      return { ok: false, error: 'empty or relative path segment' };
    }
    if (!SEGMENT_PATTERN.test(decoded)) return { ok: false, error: 'unsupported characters in path' };
    segments.push(decoded);
  }
  let versionPrefix = '';
  const first = segments[0];
  if (first !== undefined && VERSION_PATTERN.test(first)) {
    versionPrefix = `/${first}`;
    segments.shift();
  }
  if (segments.length === 0) return { ok: false, error: 'empty path' };
  const query = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search);
  return { ok: true, value: { versionPrefix, segments, route: classify(method.toUpperCase(), segments), query, search } };
}

/**
 * Rebuilds the forwarded path from validated segments. When the server has
 * resolved the container/network/exec to its full id, that id replaces the
 * caller's (possibly abbreviated or name-based) reference.
 */
export function buildForwardPath(parsed: ParsedPath, fullId?: string): string {
  const segments = [...parsed.segments];
  const kind = parsed.route.kind;
  if (fullId !== undefined && /^[A-Fa-f0-9]{12,128}$/.test(fullId) && (kind === 'container' || kind === 'exec' || kind === 'network')) {
    segments[1] = fullId;
  }
  return `${parsed.versionPrefix}/${segments.join('/')}${parsed.search}`;
}
