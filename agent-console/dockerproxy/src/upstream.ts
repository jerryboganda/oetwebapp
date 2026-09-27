// Minimal Docker daemon client over the unix socket, used only for name
// resolution (inspect calls) and the health check. Forwarding of agent
// requests lives in server.ts.
import http from 'node:http';
import { asRecord, asStringArray, str, stripSlash } from './util.js';
import type { DockerRoute } from './routes.js';
import { referencedContainers, type Resolution } from './policy.js';
import { NAME_PATTERN } from './routes.js';

export interface UpstreamResponse {
  status: number;
  headers: http.IncomingHttpHeaders;
  body: Buffer;
  json: unknown;
}

export class DockerUpstream {
  constructor(
    readonly socketPath: string,
    private readonly timeoutMs = 10_000,
    private readonly maxBytes = 8 * 1024 * 1024,
  ) {}

  get(path: string): Promise<UpstreamResponse> {
    return new Promise((resolve, reject) => {
      const req = http.request(
        { socketPath: this.socketPath, method: 'GET', path, headers: { host: 'docker', accept: 'application/json' } },
        (res) => {
          const chunks: Buffer[] = [];
          let size = 0;
          res.on('data', (chunk: Buffer) => {
            size += chunk.length;
            if (size > this.maxBytes) {
              req.destroy(new Error('Docker daemon response too large'));
              return;
            }
            chunks.push(chunk);
          });
          res.on('end', () => {
            const body = Buffer.concat(chunks);
            let json: unknown;
            try {
              json = body.length > 0 ? JSON.parse(body.toString('utf8')) : undefined;
            } catch {
              json = undefined;
            }
            resolve({ status: res.statusCode ?? 502, headers: res.headers, body, json });
          });
          res.on('error', reject);
        },
      );
      req.setTimeout(this.timeoutMs, () => req.destroy(new Error('Docker daemon timeout')));
      req.on('error', reject);
      req.end();
    });
  }
}

export interface ResolveOutcome {
  resolution: Resolution;
  /** Full id of the route's container / exec / network (used to rewrite the forwarded path). */
  fullId?: string;
  /** Daemon error (e.g. 404 No such container) to relay verbatim instead of evaluating policy. */
  failure?: UpstreamResponse;
}

/**
 * Resolves the objects a request names to canonical names so the policy sees
 * "oet-agent-console" even when the caller used an id prefix. Pure lookups:
 * GET inspect calls only.
 */
export async function resolveTargets(upstream: Pick<DockerUpstream, 'get'>, route: DockerRoute, body: unknown): Promise<ResolveOutcome> {
  const resolution: Resolution = {};
  let fullId: string | undefined;

  switch (route.kind) {
    case 'container': {
      const res = await upstream.get(`/containers/${route.id}/json`);
      if (res.status !== 200) return { resolution, failure: res };
      const doc = asRecord(res.json);
      resolution.containerName = stripSlash(str(doc.Name)) || route.id;
      fullId = str(doc.Id) || undefined;
      break;
    }
    case 'exec': {
      const res = await upstream.get(`/exec/${route.id}/json`);
      if (res.status !== 200) return { resolution, failure: res };
      const doc = asRecord(res.json);
      fullId = str(doc.ID) || undefined;
      const containerId = str(doc.ContainerID);
      if (containerId !== '' && NAME_PATTERN.test(containerId)) {
        const container = await upstream.get(`/containers/${containerId}/json`);
        if (container.status === 200) resolution.containerName = stripSlash(str(asRecord(container.json).Name));
      }
      break;
    }
    case 'network': {
      const res = await upstream.get(`/networks/${route.id}`);
      if (res.status !== 200) return { resolution, failure: res };
      const doc = asRecord(res.json);
      resolution.networkName = str(doc.Name) || route.id;
      fullId = str(doc.Id) || undefined;
      break;
    }
    case 'image': {
      const res = await upstream.get(`/images/${route.name}/json`);
      if (res.status !== 200) return { resolution, failure: res };
      resolution.imageTags = asStringArray(asRecord(res.json).RepoTags);
      break;
    }
    default:
      break;
  }

  const refs = referencedContainers(route, body).filter((ref) => NAME_PATTERN.test(ref)).slice(0, 32);
  if (refs.length > 0) {
    const names: Record<string, string> = {};
    for (const ref of refs) {
      const res = await upstream.get(`/containers/${ref}/json`);
      if (res.status === 200) {
        const name = stripSlash(str(asRecord(res.json).Name));
        if (name !== '') names[ref] = name;
      }
    }
    resolution.names = names;
  }
  return { resolution, fullId };
}
