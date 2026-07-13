import { readFileSync } from 'node:fs';
import path from 'node:path';
import { gzipSync } from 'node:zlib';

const API_TEMPLATES = [
  'scripts/deploy/nginx/api-bluegreen.conf.template',
  'scripts/deploy/nginx/router-bluegreen.conf.template',
];

function renderTemplate(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), 'utf8')
    .replaceAll('${ACTIVE_SLOT}', 'blue');
}

function apiServer(config: string) {
  let offset = 0;

  while ((offset = config.indexOf('server {', offset)) !== -1) {
    const start = offset;
    let depth = 0;
    let opened = false;

    for (; offset < config.length; offset += 1) {
      if (config[offset] === '{') {
        depth += 1;
        opened = true;
      }
      if (config[offset] === '}') depth -= 1;

      if (opened && depth === 0) {
        const block = config.slice(start, offset + 1);
        if (block.includes('listen 8080;')) return block;
        break;
      }
    }

    offset += 1;
  }

  throw new Error('API server listening on 8080 was not found');
}

function gzipTypes(server: string) {
  const directive = server.match(/^\s*gzip_types\s+([^;]+);/m);
  if (!directive) throw new Error('gzip_types directive was not found');
  return directive[1].trim().split(/\s+/);
}

function uncompressedRoutePatterns(server: string) {
  return [...server.matchAll(/if \(\$uri ~\* "([^"]+)"\)/g)]
    .map((match) => new RegExp(match[1], 'i'));
}

describe.each(API_TEMPLATES)('API router compression in %s', (template) => {
  const server = apiServer(renderTemplate(template));

  it('renders the blue/green upstream and enables useful JSON/text compression', () => {
    expect(server).toContain('http://learner-api-blue:8080');
    expect(server).toMatch(/^\s*gzip on;$/m);
    expect(server).toMatch(/^\s*gzip_vary on;$/m);
    expect(server).toMatch(/^\s*gzip_proxied any;$/m);
    expect(server).toMatch(/^\s*gzip_min_length 1024;$/m);
    expect(gzipTypes(server)).toEqual(expect.arrayContaining([
      'application/json',
      'application/problem+json',
      'application/javascript',
      'application/xml',
      'text/javascript',
      'text/xml',
      'text/plain',
    ]));
  });

  it('excludes binary, range, upgrade, event-stream, and secret-bearing responses', () => {
    expect(gzipTypes(server)).not.toEqual(expect.arrayContaining([
      'application/octet-stream',
      'application/pdf',
      'audio/mpeg',
      'video/mp4',
      'text/event-stream',
    ]));
    expect(server).toMatch(/if \(\$http_range != ""\) \{\s+gzip off;/);
    expect(server).toMatch(/if \(\$http_upgrade != ""\) \{\s+gzip off;/);
    expect(server).toContain('if ($uri ~* "^/hubs/")');
    expect(server).toContain('^/v1/(?:notifications|conversations|ai-assistant|mocks/live-room|speaking/live-rooms)/hub(?:/|$)');
    expect(server).toContain('if ($uri ~* "^/v1/auth(?:/|$)")');
    expect(server).toContain('if ($uri ~* "^/v1/admin/runtime-settings(?:/|$)")');
    expect(server).toContain('proxy_set_header Accept-Encoding "";');

    const excludedRoutes = uncompressedRoutePatterns(server);
    for (const requestPath of [
      '/V1/AUTH/sign-in',
      '/V1/ADMIN/RUNTIME-SETTINGS',
      '/V1/NOTIFICATIONS/HUB/negotiate',
      '/HUBS/WRITING-COACH',
    ]) {
      expect(excludedRoutes.some((pattern) => pattern.test(requestPath))).toBe(true);
    }
    expect(excludedRoutes.some((pattern) => pattern.test('/v1/learner/dashboard'))).toBe(false);
  });

  it('preserves upload, streaming, and proxy behavior', () => {
    expect(server).toContain('client_max_body_size 200m;');
    expect(server).toContain('proxy_http_version 1.1;');
    expect(server).toContain('proxy_set_header Upgrade $http_upgrade;');
    expect(server).toContain('proxy_set_header Connection $connection_upgrade;');
    expect(server).toContain('proxy_read_timeout 300s;');
    expect(server).toContain('proxy_send_timeout 300s;');
    expect(server).not.toMatch(/proxy_(?:request_)?buffering\s+/);
  });
});

it('meets the representative JSON payload reduction target', () => {
  const payload = Buffer.from(JSON.stringify({
    items: Array.from({ length: 100 }, (_, index) => ({
      id: `item-${index}`,
      title: 'Representative OET learning activity',
      status: 'available',
      description: 'Structured learner progress data returned by the API.',
    })),
  }));
  const compressed = gzipSync(payload, { level: 5 });

  expect(payload.byteLength).toBeGreaterThan(1024);
  expect(compressed.byteLength / payload.byteLength).toBeLessThanOrEqual(0.4);
});
