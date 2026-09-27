import { describe, expect, it } from 'vitest';
import { demuxDockerStream, parseDockerHost } from '../src/docker.js';
import { SnapshotService, parseSnapshotOutput, sanitizeLabel } from '../src/snapshot.js';

function frame(stream: 1 | 2, text: string): Buffer {
  const payload = Buffer.from(text, 'utf8');
  const header = Buffer.alloc(8);
  header[0] = stream;
  header.writeUInt32BE(payload.length, 4);
  return Buffer.concat([header, payload]);
}

function service(now: () => number, exitCode = 0) {
  const calls: { container: string; cmd: string[] }[] = [];
  const svc = new SnapshotService({
    docker: {
      exec: async (container, cmd) => {
        calls.push({ container, cmd });
        const file = `/backups/agent-snap-${cmd[2]}.dump`;
        return { exitCode, stdout: `[backup] done\nSNAPSHOT_FILE=${file}\n`, stderr: exitCode ? 'disk full' : '' };
      },
    },
    container: 'oet-db-backup',
    script: '/usr/local/bin/postgres-backup.sh',
    fullDumpMinIntervalMs: 600_000,
    timeoutMs: 60_000,
    now,
  });
  return { svc, calls };
}

describe('SnapshotService', () => {
  it('runs the backup script in oet-db-backup with --snapshot and --table', async () => {
    const { svc, calls } = service(() => 0);
    const result = await svc.take({ label: 's01abc-20260927T101010', tables: ['scratch'] });
    expect(result).toEqual({ ok: true, label: 's01abc-20260927T101010', file: '/backups/agent-snap-s01abc-20260927T101010.dump' });
    expect(calls).toEqual([
      { container: 'oet-db-backup', cmd: ['/usr/local/bin/postgres-backup.sh', '--snapshot', 's01abc-20260927T101010', '--table', 'scratch'] },
    ]);
  });

  it('rate-limits full dumps to one per 10 minutes but not table-scoped ones', async () => {
    let now = 0;
    const { svc, calls } = service(() => now);
    expect((await svc.take({ label: 'a' })).ok).toBe(true);
    now += 5 * 60_000;
    const limited = await svc.take({ label: 'b' });
    expect(limited.ok).toBe(false);
    expect(limited.error).toMatch(/one per 10 min/);
    expect((await svc.take({ label: 'c', tables: ['scratch'] })).ok).toBe(true);
    now += 6 * 60_000;
    expect((await svc.take({ label: 'd' })).ok).toBe(true);
    expect(calls).toHaveLength(3);
  });

  it('reports script failures and rejects unsafe table names', async () => {
    const { svc } = service(() => 0, 1);
    const failed = await svc.take({ label: 'x', tables: ['scratch'] });
    expect(failed.ok).toBe(false);
    expect(failed.error).toMatch(/exited with 1/);

    const ok = service(() => 0);
    const bad = await ok.svc.take({ label: 'x', tables: ['users; DROP TABLE x'] });
    expect(bad).toMatchObject({ ok: false });
    expect(ok.calls).toHaveLength(0);
  });

  it('passes quoted identifiers through for case-sensitive tables', async () => {
    const { svc, calls } = service(() => 0);
    expect((await svc.take({ label: 'q', tables: ['"WritingSubmissions"'] })).ok).toBe(true);
    expect(calls[0]?.cmd.slice(-2)).toEqual(['--table', '"WritingSubmissions"']);
  });

  it('sanitizes labels and parses the reported file', () => {
    expect(sanitizeLabel('sess 01/..;rm -rf')).toBe('sess-01-rm-rf');
    expect(sanitizeLabel('')).toBe('snapshot');
    expect(sanitizeLabel('x'.repeat(100))).toHaveLength(64);
    expect(parseSnapshotOutput('noise\nSNAPSHOT_FILE=/backups/agent-snap-1.dump.gpg\n')).toBe('/backups/agent-snap-1.dump.gpg');
    expect(parseSnapshotOutput('noise\n/backups/agent-snap-2.dump\n')).toBe('/backups/agent-snap-2.dump');
    expect(parseSnapshotOutput('no path here')).toBeNull();
  });
});

describe('docker helpers', () => {
  it('demultiplexes attach streams', () => {
    const buf = Buffer.concat([frame(1, 'hello '), frame(2, 'warn'), frame(1, 'world')]);
    expect(demuxDockerStream(buf)).toEqual({ stdout: 'hello world', stderr: 'warn' });
    expect(demuxDockerStream(Buffer.from('raw tty output'))).toEqual({ stdout: 'raw tty output', stderr: '' });
  });

  it('parses DOCKER_HOST', () => {
    expect(parseDockerHost('tcp://oet-agent-dockerproxy:2375')).toEqual({ host: 'oet-agent-dockerproxy', port: 2375 });
    expect(parseDockerHost('unix:///var/run/docker.sock')).toEqual({ socketPath: '/var/run/docker.sock' });
  });
});
