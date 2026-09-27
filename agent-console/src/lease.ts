import type { DockerClient } from './docker.js';
import { describeError } from './errors.js';
import type { Runner } from './exec.js';
import type { Logger } from './log.js';

// Owner lease (dead-man switch) and kill switch (plan "src/lease.ts / kill switch").
//
// Lease: the browser heartbeats every 60 s → API → POST /v1/lease; the
// server clamps the expiry to now + 3 min. When it lapses: Autopilot sessions
// drop to Guarded, new turns are refused (423) and running turns pause at
// their next tool boundary until the lease is renewed.
//
// Kill switch: POST /v1/admin/stop-all aborts every turn, denies every pending
// approval, `pkill -9 -u 10002`, and stops containers labelled
// `oet.agent.session` through the docker proxy with the control token. The
// console then stays `killed` (new turns 423) until POST /v1/admin/drain
// {draining:false} resumes it.

type Listener = () => void;

export interface LeaseOptions {
  maxMs: number;
  now?: () => number;
  checkIntervalMs?: number;
}

export class LeaseManager {
  private expires: number | null = null;
  private active = false;
  private readonly expiredListeners = new Set<Listener>();
  private readonly renewedListeners = new Set<Listener>();
  private readonly waiters = new Set<() => void>();
  private timer: NodeJS.Timeout | null = null;
  private readonly now: () => number;

  constructor(private readonly options: LeaseOptions) {
    this.now = options.now ?? Date.now;
  }

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => this.tick(), this.options.checkIntervalMs ?? 5_000);
    this.timer.unref();
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  /** Sets the lease from the requested expiry, clamped to [now, now + maxMs]. Returns the effective ISO expiry. */
  set(requested: string | number): string | null {
    const at = typeof requested === 'number' ? requested : Date.parse(requested);
    if (!Number.isFinite(at)) throw new TypeError('expiresAt must be an ISO-8601 timestamp');
    const now = this.now();
    const clamped = Math.min(at, now + this.options.maxMs);
    this.expires = clamped > now ? clamped : null;
    this.tick();
    return this.expiresAt();
  }

  isActive(): boolean {
    return this.expires !== null && this.expires > this.now();
  }

  expiresAt(): string | null {
    return this.isActive() && this.expires !== null ? new Date(this.expires).toISOString() : null;
  }

  onExpired(listener: Listener): () => void {
    this.expiredListeners.add(listener);
    return () => this.expiredListeners.delete(listener);
  }

  onRenewed(listener: Listener): () => void {
    this.renewedListeners.add(listener);
    return () => this.renewedListeners.delete(listener);
  }

  /** Detects active↔expired transitions (called by the interval and by set()). */
  tick(): void {
    const nowActive = this.isActive();
    if (this.active && !nowActive) {
      this.active = false;
      for (const listener of [...this.expiredListeners]) listener();
    } else if (!this.active && nowActive) {
      this.active = true;
      for (const listener of [...this.renewedListeners]) listener();
      for (const wake of [...this.waiters]) wake();
    }
  }

  /** Resolves true once the lease is active, false on timeout or abort. */
  waitForActive(signal: AbortSignal | undefined, timeoutMs: number): Promise<boolean> {
    if (this.isActive()) return Promise.resolve(true);
    return new Promise<boolean>((resolve) => {
      let timer: NodeJS.Timeout | undefined;
      const done = (value: boolean): void => {
        this.waiters.delete(wake);
        if (timer) clearTimeout(timer);
        signal?.removeEventListener('abort', onAbort);
        resolve(value);
      };
      const wake = (): void => done(true);
      const onAbort = (): void => done(false);
      this.waiters.add(wake);
      timer = setTimeout(() => done(false), timeoutMs);
      timer.unref();
      if (signal?.aborted) done(false);
      else signal?.addEventListener('abort', onAbort, { once: true });
    });
  }
}

export class ControlState {
  killed = false;
  draining = false;
}

export interface StopAllDeps {
  /** Aborts every running turn; returns how many were stopped. */
  abortAllTurns(): Promise<number>;
  /** Denies every pending approval (by: kill). */
  cancelApprovals(): number;
  killAgentProcesses(): Promise<number>;
  stopSessionContainers(): Promise<number>;
  /** Drops every approve-for-session grant cached by the egress/docker proxies. */
  revokeProxyGrants?(): Promise<void>;
  logger?: Logger;
}

export async function stopAll(state: ControlState, deps: StopAllDeps): Promise<{ stoppedTurns: number; killedProcesses: number }> {
  state.killed = true;
  deps.cancelApprovals();
  const stoppedTurns = await deps.abortAllTurns().catch((error: unknown) => {
    deps.logger?.error({ err: error }, 'stop-all: aborting turns failed');
    return 0;
  });
  const killedProcesses = await deps.killAgentProcesses().catch((error: unknown) => {
    deps.logger?.error({ err: error }, 'stop-all: pkill failed');
    return 0;
  });
  await deps.stopSessionContainers().catch((error: unknown) => {
    deps.logger?.error({ err: describeError(error) }, 'stop-all: stopping session containers failed');
    return 0;
  });
  await deps.revokeProxyGrants?.().catch((error: unknown) => {
    deps.logger?.error({ err: describeError(error) }, 'stop-all: revoking proxy grants failed');
  });
  return { stoppedTurns, killedProcesses };
}

/** `pkill -9 -u <uid>`; returns the number of processes that were running as that uid. */
export async function killAgentProcesses(run: Runner, uid: number): Promise<number> {
  const list = await run('pgrep', ['-u', String(uid)], { timeoutMs: 10_000 });
  const count = list.code === 0 ? list.stdout.split('\n').filter((l) => l.trim()).length : 0;
  if (count > 0) await run('pkill', ['-9', '-u', String(uid)], { timeoutMs: 10_000 });
  return count;
}

export const SESSION_CONTAINER_LABEL = 'oet.agent.session';

export async function stopSessionContainers(docker: Pick<DockerClient, 'listByLabel' | 'stop'>, logger?: Logger): Promise<number> {
  const containers = await docker.listByLabel(SESSION_CONTAINER_LABEL);
  let stopped = 0;
  for (const container of containers) {
    try {
      await docker.stop(container.Id, 2);
      stopped += 1;
    } catch (error) {
      logger?.warn({ err: describeError(error), id: container.Id }, 'failed to stop session container');
    }
  }
  return stopped;
}
