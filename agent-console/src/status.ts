import { existsSync } from 'node:fs';
import type { ApprovalRequest, ConsoleStatus, Engine, EngineStatus, GithubStatus } from './contract.js';
import { SYSTEM_SESSION_ID } from './contract.js';
import { describeError } from './errors.js';
import type { ControlState, LeaseManager } from './lease.js';
import type { Logger } from './log.js';

// ConsoleStatus aggregation (CONTRACT.md §3 GET /v1/status). Never throws:
// a failing engine or GitHub lookup is reported inside its own block.

export interface StatusDeps {
  version: string;
  maxConcurrentTurns: number;
  updatePendingFile: string | null;
  control: ControlState;
  lease: LeaseManager;
  activeTurns: () => number;
  engineStatus: (engine: Engine) => Promise<EngineStatus>;
  githubStatus: () => Promise<GithubStatus>;
  systemApprovals: () => ApprovalRequest[];
  logger?: Logger;
  fileExists?: (file: string) => boolean;
}

/** The deploy workflow drops a marker file when a newer image is pulled but not yet applied. */
export function isUpdatePending(file: string | null, fileExists: (file: string) => boolean = existsSync): boolean {
  if (!file) return false;
  try {
    return fileExists(file);
  } catch {
    return false;
  }
}

export async function buildStatus(deps: StatusDeps): Promise<ConsoleStatus> {
  const [claude, codex, github] = await Promise.all([
    deps.engineStatus('claude'),
    deps.engineStatus('codex'),
    deps.githubStatus().catch((error: unknown): GithubStatus => {
      deps.logger?.warn({ err: describeError(error) }, 'github status failed');
      return { agentTokenSet: false, shipTokenSet: false };
    }),
  ]);
  return {
    version: deps.version,
    draining: deps.control.draining,
    killed: deps.control.killed,
    updatePending: isUpdatePending(deps.updatePendingFile, deps.fileExists),
    activeTurns: deps.activeTurns(),
    maxConcurrentTurns: deps.maxConcurrentTurns,
    lease: { expiresAt: deps.lease.expiresAt() },
    engines: { claude, codex },
    github,
    systemApprovals: deps.systemApprovals(),
    systemSessionId: SYSTEM_SESSION_ID,
  };
}
