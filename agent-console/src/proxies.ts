import { describeError } from './errors.js';
import type { Logger } from './log.js';

// Control-plane calls to the egress and docker proxies (CONTRACT.md §6).
// Both proxies cache "approve for session" grants; they expose
//   DELETE /internal/sessions/:id   (one session)
//   DELETE /internal/sessions       (all sessions)
// guarded by X-Oet-Control-Token. The sidecar calls them when a session is
// tainted (grants are dropped — plan "Taint"), archived or handed off, and on
// the kill switch. Best-effort: a proxy that is down has no grants to drop.

export type HttpDelete = (url: string, headers: Record<string, string>, timeoutMs: number) => Promise<number>;

const fetchDelete: HttpDelete = async (url, headers, timeoutMs) => {
  const res = await fetch(url, { method: 'DELETE', headers, signal: AbortSignal.timeout(timeoutMs) });
  await res.arrayBuffer().catch(() => undefined);
  return res.status;
};

export interface ProxyGrantOptions {
  egressProxyUrl: string;
  dockerHost: string;
  controlToken: string | null;
  logger?: Logger;
  httpDelete?: HttpDelete;
  timeoutMs?: number;
}

/** Base http:// URL of a proxy from its proxy/DOCKER_HOST URL (credentials stripped). */
export function proxyBaseUrl(raw: string): string | null {
  try {
    const url = new URL(raw.replace(/^tcp:\/\//, 'http://'));
    if (url.protocol !== 'http:') return null;
    return `http://${url.host}`;
  } catch {
    return null;
  }
}

export class ProxyGrants {
  private readonly bases: string[];
  private readonly httpDelete: HttpDelete;

  constructor(private readonly options: ProxyGrantOptions) {
    this.bases = [proxyBaseUrl(options.egressProxyUrl), proxyBaseUrl(options.dockerHost)].filter((b): b is string => b !== null);
    this.httpDelete = options.httpDelete ?? fetchDelete;
  }

  revokeSession(sessionId: string): Promise<void> {
    return this.send(`/internal/sessions/${encodeURIComponent(sessionId)}`);
  }

  revokeAll(): Promise<void> {
    return this.send('/internal/sessions');
  }

  private async send(path: string): Promise<void> {
    const token = this.options.controlToken;
    if (!token) return;
    await Promise.all(
      this.bases.map(async (base) => {
        try {
          const status = await this.httpDelete(`${base}${path}`, { 'X-Oet-Control-Token': token }, this.options.timeoutMs ?? 5_000);
          if (status >= 400 && status !== 404) this.options.logger?.warn({ base, path, status }, 'proxy grant revocation refused');
        } catch (error) {
          this.options.logger?.warn({ base, path, err: describeError(error) }, 'proxy grant revocation failed');
        }
      }),
    );
  }
}
