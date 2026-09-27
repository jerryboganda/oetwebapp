import http from 'node:http';

// Minimal Docker Engine API client for control-plane calls through
// oet-agent-dockerproxy (CONTRACT.md §6): every request carries
// X-Oet-Control-Token so the proxy bypasses the agent policy. Used for
// pre-snapshots (exec in oet-db-backup) and kill-switch container cleanup.

export interface DockerResponse {
  status: number;
  body: Buffer;
}

export interface DockerTransport {
  request(method: string, path: string, body?: unknown, timeoutMs?: number): Promise<DockerResponse>;
}

export interface ExecResult {
  exitCode: number | null;
  stdout: string;
  stderr: string;
}

export class DockerError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = 'DockerError';
    this.status = status;
  }
}

interface Endpoint {
  host?: string;
  port?: number;
  socketPath?: string;
}

export function parseDockerHost(dockerHost: string): Endpoint {
  if (dockerHost.startsWith('unix://')) return { socketPath: dockerHost.slice('unix://'.length) };
  const url = new URL(dockerHost.replace(/^tcp:\/\//, 'http://'));
  return { host: url.hostname, port: url.port ? Number(url.port) : 2375 };
}

/** HTTP transport over DOCKER_HOST (tcp:// or unix://). */
export function createHttpTransport(dockerHost: string, controlToken: string | null): DockerTransport {
  const endpoint = parseDockerHost(dockerHost);
  return {
    request(method, path, body, timeoutMs = 60_000) {
      if (!controlToken) {
        return Promise.reject(new DockerError(0, 'Proxy control token is not configured; control-plane docker calls are disabled.'));
      }
      return new Promise<DockerResponse>((resolve, reject) => {
        const payload = body === undefined ? undefined : Buffer.from(JSON.stringify(body));
        const req = http.request(
          {
            ...endpoint,
            method,
            path,
            headers: {
              'X-Oet-Control-Token': controlToken,
              Host: 'docker',
              ...(payload ? { 'Content-Type': 'application/json', 'Content-Length': String(payload.length) } : {}),
            },
          },
          (res) => {
            const chunks: Buffer[] = [];
            res.on('data', (chunk: Buffer) => chunks.push(chunk));
            res.on('end', () => resolve({ status: res.statusCode ?? 0, body: Buffer.concat(chunks) }));
            res.on('error', reject);
          },
        );
        req.setTimeout(timeoutMs, () => req.destroy(new Error(`docker ${method} ${path} timed out`)));
        req.on('error', reject);
        req.end(payload);
      });
    },
  };
}

/**
 * Splits Docker's multiplexed attach stream (8-byte frame headers: stream,
 * 0,0,0, uint32 BE size). Falls back to raw stdout when the header is absent
 * (TTY exec).
 */
export function demuxDockerStream(buffer: Buffer): { stdout: string; stderr: string } {
  const out: Buffer[] = [];
  const err: Buffer[] = [];
  let offset = 0;
  while (offset + 8 <= buffer.length) {
    const stream = buffer[offset];
    const valid = (stream === 0 || stream === 1 || stream === 2) && buffer[offset + 1] === 0 && buffer[offset + 2] === 0 && buffer[offset + 3] === 0;
    if (!valid) break;
    const size = buffer.readUInt32BE(offset + 4);
    const start = offset + 8;
    const end = Math.min(start + size, buffer.length);
    (stream === 2 ? err : out).push(buffer.subarray(start, end));
    offset = end;
  }
  if (offset === 0 && buffer.length > 0) return { stdout: buffer.toString('utf8'), stderr: '' };
  if (offset < buffer.length) out.push(buffer.subarray(offset));
  return { stdout: Buffer.concat(out).toString('utf8'), stderr: Buffer.concat(err).toString('utf8') };
}

export class DockerClient {
  constructor(private readonly transport: DockerTransport) {}

  async json<T>(method: string, path: string, body?: unknown, timeoutMs?: number): Promise<T> {
    const res = await this.transport.request(method, path, body, timeoutMs);
    if (res.status < 200 || res.status >= 300) {
      throw new DockerError(res.status, `docker ${method} ${path} failed with HTTP ${res.status}: ${res.body.toString('utf8').slice(0, 300)}`);
    }
    return (res.body.length ? JSON.parse(res.body.toString('utf8')) : null) as T;
  }

  /** Runs a command in a container and waits for it (non-TTY, attached). */
  async exec(container: string, cmd: string[], timeoutMs: number): Promise<ExecResult> {
    const created = await this.json<{ Id: string }>('POST', `/containers/${encodeURIComponent(container)}/exec`, {
      AttachStdout: true,
      AttachStderr: true,
      AttachStdin: false,
      Tty: false,
      Cmd: cmd,
    });
    const started = await this.transport.request('POST', `/exec/${encodeURIComponent(created.Id)}/start`, { Detach: false, Tty: false }, timeoutMs);
    if (started.status < 200 || started.status >= 300) {
      throw new DockerError(started.status, `docker exec start failed with HTTP ${started.status}`);
    }
    const { stdout, stderr } = demuxDockerStream(started.body);
    const inspected = await this.json<{ ExitCode: number | null; Running: boolean }>('GET', `/exec/${encodeURIComponent(created.Id)}/json`);
    return { exitCode: inspected.Running ? null : inspected.ExitCode, stdout, stderr };
  }

  /** Containers carrying `label` (any value). */
  async listByLabel(label: string): Promise<{ Id: string; Names: string[] }[]> {
    const filters = encodeURIComponent(JSON.stringify({ label: [label] }));
    return this.json<{ Id: string; Names: string[] }[]>('GET', `/containers/json?all=false&filters=${filters}`);
  }

  async stop(id: string, graceSeconds = 2): Promise<void> {
    const res = await this.transport.request('POST', `/containers/${encodeURIComponent(id)}/stop?t=${graceSeconds}`, undefined, 30_000);
    if (res.status !== 204 && res.status !== 304 && res.status !== 404) {
      throw new DockerError(res.status, `docker stop ${id} failed with HTTP ${res.status}`);
    }
  }
}
