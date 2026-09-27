import { describe, expect, it, vi } from 'vitest';
import { ControlState, LeaseManager, killAgentProcesses, stopAll, stopSessionContainers } from '../src/lease.js';
import type { RunResult, Runner } from '../src/exec.js';

describe('LeaseManager', () => {
  it('clamps the requested expiry to now + 3 minutes', () => {
    let now = 1_000_000;
    const lease = new LeaseManager({ maxMs: 180_000, now: () => now });
    expect(lease.isActive()).toBe(false);
    const effective = lease.set(now + 3_600_000);
    expect(effective).toBe(new Date(now + 180_000).toISOString());
    expect(lease.isActive()).toBe(true);
    now += 180_001;
    expect(lease.isActive()).toBe(false);
    expect(lease.expiresAt()).toBeNull();
  });

  it('treats a past expiry as releasing the lease', () => {
    const now = 1_000_000;
    const lease = new LeaseManager({ maxMs: 180_000, now: () => now });
    lease.set(now + 60_000);
    expect(lease.set(new Date(now - 1).toISOString())).toBeNull();
    expect(lease.isActive()).toBe(false);
  });

  it('rejects garbage timestamps', () => {
    const lease = new LeaseManager({ maxMs: 180_000 });
    expect(() => lease.set('not-a-date')).toThrow();
  });

  it('fires expired/renewed transitions once each', () => {
    let now = 1_000_000;
    const lease = new LeaseManager({ maxMs: 180_000, now: () => now });
    const expired = vi.fn();
    const renewed = vi.fn();
    lease.onExpired(expired);
    lease.onRenewed(renewed);
    lease.set(now + 60_000);
    expect(renewed).toHaveBeenCalledTimes(1);
    now += 30_000;
    lease.tick();
    expect(expired).not.toHaveBeenCalled();
    now += 31_000;
    lease.tick();
    lease.tick();
    expect(expired).toHaveBeenCalledTimes(1);
    lease.set(now + 60_000);
    expect(renewed).toHaveBeenCalledTimes(2);
  });

  it('wakes paused tool calls when the lease is renewed, or times out', async () => {
    let now = 1_000_000;
    const lease = new LeaseManager({ maxMs: 180_000, now: () => now });
    const waiting = lease.waitForActive(undefined, 60_000);
    lease.set(now + 60_000);
    await expect(waiting).resolves.toBe(true);

    now += 120_000;
    lease.tick();
    const controller = new AbortController();
    const aborted = lease.waitForActive(controller.signal, 60_000);
    controller.abort();
    await expect(aborted).resolves.toBe(false);
  });
});

describe('kill switch', () => {
  it('stops turns, denies approvals, kills agent processes and session containers', async () => {
    const state = new ControlState();
    const calls: string[] = [];
    const result = await stopAll(state, {
      abortAllTurns: async () => {
        calls.push('turns');
        return 2;
      },
      cancelApprovals: () => {
        calls.push('approvals');
        return 1;
      },
      killAgentProcesses: async () => {
        calls.push('pkill');
        return 5;
      },
      stopSessionContainers: async () => {
        calls.push('containers');
        return 1;
      },
      revokeProxyGrants: async () => {
        calls.push('proxy-grants');
      },
    });
    expect(result).toEqual({ stoppedTurns: 2, killedProcesses: 5 });
    expect(state.killed).toBe(true);
    expect(calls).toEqual(['approvals', 'turns', 'pkill', 'containers', 'proxy-grants']);
  });

  it('keeps going when one step fails', async () => {
    const state = new ControlState();
    const result = await stopAll(state, {
      abortAllTurns: async () => {
        throw new Error('boom');
      },
      cancelApprovals: () => 0,
      killAgentProcesses: async () => 1,
      stopSessionContainers: async () => {
        throw new Error('proxy down');
      },
    });
    expect(result).toEqual({ stoppedTurns: 0, killedProcesses: 1 });
  });

  it('pkill -9 -u targets only the agent uid', async () => {
    const invocations: string[][] = [];
    const result = (stdout: string, code = 0): RunResult => ({ code, signal: null, stdout, stdoutBuffer: Buffer.from(stdout), stderr: '', stdoutTruncated: false, timedOut: false });
    const run: Runner = async (command, args) => {
      invocations.push([command, ...args]);
      return command === 'pgrep' ? result('101\n102\n103\n') : result('');
    };
    await expect(killAgentProcesses(run, 10002)).resolves.toBe(3);
    expect(invocations).toEqual([
      ['pgrep', '-u', '10002'],
      ['pkill', '-9', '-u', '10002'],
    ]);
  });

  it('stops only containers labelled oet.agent.session', async () => {
    const stopped: string[] = [];
    const labels: string[] = [];
    const count = await stopSessionContainers({
      listByLabel: async (label) => {
        labels.push(label);
        return [
          { Id: 'c1', Names: ['/agent-scratch-1'] },
          { Id: 'c2', Names: ['/agent-scratch-2'] },
        ];
      },
      stop: async (id) => {
        stopped.push(id);
      },
    });
    expect(labels).toEqual(['oet.agent.session']);
    expect(stopped).toEqual(['c1', 'c2']);
    expect(count).toBe(2);
  });
});
