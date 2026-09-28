// Kept byte-identical in egress/src/ and dockerproxy/src/ (drift check:
// agent-console/tests/proxy-shared-files.test.ts). Edit both copies.
//
// Approval callback to the sidecar (CONTRACT.md §6):
//   POST http://oet-agent-console:8410/internal/approvals
//   X-Oet-Proxy-Token: <shared proxy token>
//   { source, sessionId, summary, target, reasons, details? }
//   → blocks until decided (≤ 600 s) → { decision: "approve" | "deny", scope: "once" | "session" }
//
// Uses node:http rather than fetch(): undici's default headersTimeout (300 s)
// would abort a legitimately long owner decision. Every failure mode (timeout,
// transport error, bad status, malformed body) fails CLOSED as a deny.
import http from 'node:http';

export type ApprovalSource = 'egress' | 'docker';

export interface ApprovalRequestBody {
  source: ApprovalSource;
  sessionId: string | null;
  summary: string;
  target: string;
  reasons: string[];
  details?: Record<string, unknown>;
}

export interface ApprovalResult {
  decision: 'approve' | 'deny';
  scope: 'once' | 'session';
  /** Set when the deny was produced locally (timeout / transport / protocol error). */
  error?: string;
  /** Served from the short-lived deny cache without calling the sidecar. */
  cached?: boolean;
  /** Joined an identical approval that was already pending. */
  coalesced?: boolean;
}

export type ApprovalTransport = (body: ApprovalRequestBody) => Promise<ApprovalResult>;

const MAX_RESPONSE_BYTES = 64 * 1024;

function localDeny(error: string): ApprovalResult {
  return { decision: 'deny', scope: 'once', error };
}

/** Validates the sidecar's answer. Anything unexpected is a deny. */
export function parseApprovalResponse(status: number, text: string): ApprovalResult {
  if (status < 200 || status >= 300) {
    return localDeny(`approval endpoint returned HTTP ${status}`);
  }
  let json: unknown;
  try {
    json = JSON.parse(text);
  } catch {
    return localDeny('approval endpoint returned invalid JSON');
  }
  if (json === null || typeof json !== 'object' || Array.isArray(json)) {
    return localDeny('approval endpoint returned a non-object body');
  }
  const record = json as Record<string, unknown>;
  if (record.decision !== 'approve' && record.decision !== 'deny') {
    return localDeny('approval endpoint returned an unknown decision');
  }
  return { decision: record.decision, scope: record.scope === 'session' ? 'session' : 'once' };
}

export interface HttpApprovalTransportOptions {
  url: string;
  token: string;
  timeoutMs: number;
}

export function createHttpApprovalTransport(opts: HttpApprovalTransportOptions): ApprovalTransport {
  const target = new URL(opts.url);
  if (target.protocol !== 'http:') {
    throw new Error('OWNER_AGENT_APPROVAL_URL must be an http:// URL on the internal oet_agent_net network');
  }
  return (body) =>
    new Promise<ApprovalResult>((resolve) => {
      let settled = false;
      let timer: NodeJS.Timeout | undefined;
      const finish = (result: ApprovalResult): void => {
        if (settled) return;
        settled = true;
        if (timer) clearTimeout(timer);
        resolve(result);
      };
      const payload = Buffer.from(JSON.stringify(body), 'utf8');
      const req = http.request(
        {
          protocol: 'http:',
          hostname: target.hostname,
          port: target.port === '' ? 80 : Number(target.port),
          path: `${target.pathname}${target.search}`,
          method: 'POST',
          agent: false,
          headers: {
            'content-type': 'application/json',
            'content-length': String(payload.length),
            accept: 'application/json',
            'x-oet-proxy-token': opts.token,
          },
        },
        (res) => {
          const chunks: Buffer[] = [];
          let size = 0;
          res.on('data', (chunk: Buffer) => {
            size += chunk.length;
            if (size > MAX_RESPONSE_BYTES) {
              req.destroy();
              finish(localDeny('approval response too large'));
              return;
            }
            chunks.push(chunk);
          });
          res.on('end', () => {
            finish(parseApprovalResponse(res.statusCode ?? 0, Buffer.concat(chunks).toString('utf8')));
          });
          res.on('error', (err) => finish(localDeny(`approval response error: ${err.message}`)));
        },
      );
      timer = setTimeout(() => {
        req.destroy();
        finish(localDeny('approval timed out'));
      }, opts.timeoutMs);
      req.on('error', (err) => finish(localDeny(`approval request failed: ${err.message}`)));
      req.end(payload);
    });
}

export interface ApprovalBrokerOptions {
  /** How long a deny for the same coalesce key is replayed without a new card (0 = off). */
  denyCacheMs: number;
  now?: () => number;
  maxCachedDenials?: number;
}

/**
 * Coalesces identical concurrent approvals (a client opening 10 connections to
 * the same unknown host produces ONE owner card) and replays recent denials
 * briefly so a retry loop cannot flood the console with cards.
 */
export class ApprovalBroker {
  private readonly pending = new Map<string, Promise<ApprovalResult>>();
  private readonly recentDenials = new Map<string, number>();
  private readonly now: () => number;
  private readonly maxCachedDenials: number;

  constructor(
    private readonly transport: ApprovalTransport,
    private readonly options: ApprovalBrokerOptions,
  ) {
    this.now = options.now ?? Date.now;
    this.maxCachedDenials = options.maxCachedDenials ?? 1000;
  }

  request(body: ApprovalRequestBody, coalesceKey?: string): Promise<ApprovalResult> {
    if (coalesceKey !== undefined) {
      const until = this.recentDenials.get(coalesceKey);
      if (until !== undefined) {
        if (until > this.now()) {
          return Promise.resolve({ decision: 'deny', scope: 'once', cached: true });
        }
        this.recentDenials.delete(coalesceKey);
      }
      const existing = this.pending.get(coalesceKey);
      if (existing) {
        return existing.then((result) => ({ ...result, coalesced: true }));
      }
    }
    const promise = this.transport(body)
      .catch((err: unknown) => localDeny(`approval transport failed: ${err instanceof Error ? err.message : String(err)}`))
      .then((result) => {
        if (coalesceKey !== undefined) {
          this.pending.delete(coalesceKey);
          if (result.decision === 'deny' && this.options.denyCacheMs > 0) {
            this.rememberDenial(coalesceKey);
          }
        }
        return result;
      });
    if (coalesceKey !== undefined) {
      this.pending.set(coalesceKey, promise);
    }
    return promise;
  }

  private rememberDenial(key: string): void {
    if (this.recentDenials.size >= this.maxCachedDenials) {
      const now = this.now();
      for (const [k, until] of this.recentDenials) {
        if (until <= now) this.recentDenials.delete(k);
      }
      if (this.recentDenials.size >= this.maxCachedDenials) {
        const oldest = this.recentDenials.keys().next();
        if (!oldest.done) this.recentDenials.delete(oldest.value);
      }
    }
    this.recentDenials.set(key, this.now() + this.options.denyCacheMs);
  }
}
