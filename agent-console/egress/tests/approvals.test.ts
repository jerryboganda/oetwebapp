import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import {
  ApprovalBroker,
  createHttpApprovalTransport,
  parseApprovalResponse,
  type ApprovalRequestBody,
  type ApprovalResult,
} from '../src/approvals.js';

const TOKEN = 'test-proxy-token-0123456789abcdefABCDEF';

function body(target = 'example.com:443'): ApprovalRequestBody {
  return { source: 'egress', sessionId: null, summary: `CONNECT ${target}`, target, reasons: ['test'] };
}

describe('parseApprovalResponse', () => {
  it.each([
    [200, '{"decision":"approve","scope":"session"}', { decision: 'approve', scope: 'session' }],
    [200, '{"decision":"approve"}', { decision: 'approve', scope: 'once' }],
    [200, '{"decision":"deny","scope":"once"}', { decision: 'deny', scope: 'once' }],
  ])('accepts HTTP %i %s', (status, text, expected) => {
    expect(parseApprovalResponse(status, text)).toEqual(expected);
  });

  it.each([
    [500, '{"decision":"approve"}'],
    [200, 'not json'],
    [200, '[]'],
    [200, '{"decision":"yes"}'],
    [401, ''],
  ])('fails closed on HTTP %i %s', (status, text) => {
    const result = parseApprovalResponse(status, text);
    expect(result.decision).toBe('deny');
    expect(result.error).toBeTruthy();
  });
});

describe('ApprovalBroker', () => {
  it('coalesces identical concurrent requests into one sidecar call', async () => {
    let calls = 0;
    let release: (r: ApprovalResult) => void = () => undefined;
    const broker = new ApprovalBroker(
      () => {
        calls += 1;
        return new Promise<ApprovalResult>((resolve) => {
          release = resolve;
        });
      },
      { denyCacheMs: 0 },
    );
    const a = broker.request(body(), 'k');
    const b = broker.request(body(), 'k');
    release({ decision: 'approve', scope: 'once' });
    const [ra, rb] = await Promise.all([a, b]);
    expect(calls).toBe(1);
    expect(ra.decision).toBe('approve');
    expect(rb).toMatchObject({ decision: 'approve', coalesced: true });
  });

  it('replays a recent deny without a new card, then expires it', async () => {
    let now = 1_000;
    let calls = 0;
    const broker = new ApprovalBroker(
      async () => {
        calls += 1;
        return { decision: 'deny', scope: 'once' };
      },
      { denyCacheMs: 60_000, now: () => now },
    );
    await broker.request(body(), 'k');
    const cached = await broker.request(body(), 'k');
    expect(cached).toMatchObject({ decision: 'deny', cached: true });
    expect(calls).toBe(1);
    now += 60_001;
    await broker.request(body(), 'k');
    expect(calls).toBe(2);
  });

  it('turns a throwing transport into a deny', async () => {
    const broker = new ApprovalBroker(
      async () => {
        throw new Error('boom');
      },
      { denyCacheMs: 0 },
    );
    const result = await broker.request(body());
    expect(result.decision).toBe('deny');
    expect(result.error).toContain('boom');
  });
});

describe('createHttpApprovalTransport', () => {
  let server: http.Server | undefined;

  afterEach(async () => {
    if (server) {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server?.close(() => resolve()));
      server = undefined;
    }
  });

  async function start(handler: http.RequestListener): Promise<string> {
    server = http.createServer(handler);
    await new Promise<void>((resolve) => server?.listen(0, '127.0.0.1', () => resolve()));
    const { port } = server.address() as AddressInfo;
    return `http://127.0.0.1:${port}/internal/approvals`;
  }

  it('posts the contract body with the proxy token and returns the decision', async () => {
    let seenToken: string | undefined;
    let seenBody: unknown;
    const url = await start((req, res) => {
      seenToken = req.headers['x-oet-proxy-token'] as string | undefined;
      const chunks: Buffer[] = [];
      req.on('data', (c: Buffer) => chunks.push(c));
      req.on('end', () => {
        seenBody = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end('{"decision":"approve","scope":"session"}');
      });
    });
    const transport = createHttpApprovalTransport({ url, token: TOKEN, timeoutMs: 5_000 });
    const result = await transport(body());
    expect(result).toEqual({ decision: 'approve', scope: 'session' });
    expect(seenToken).toBe(TOKEN);
    expect(seenBody).toEqual(body());
  });

  it('denies when the sidecar does not answer in time', async () => {
    const url = await start(() => {
      // never responds
    });
    const transport = createHttpApprovalTransport({ url, token: TOKEN, timeoutMs: 200 });
    const result = await transport(body());
    expect(result).toMatchObject({ decision: 'deny', error: 'approval timed out' });
  });

  it('denies when the sidecar is unreachable', async () => {
    const transport = createHttpApprovalTransport({ url: 'http://127.0.0.1:1/internal/approvals', token: TOKEN, timeoutMs: 2_000 });
    const result = await transport(body());
    expect(result.decision).toBe('deny');
  });

  it('refuses a non-http approval URL', () => {
    expect(() => createHttpApprovalTransport({ url: 'https://example.com/x', token: TOKEN, timeoutMs: 1 })).toThrow();
  });
});
