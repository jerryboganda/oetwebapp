import { type WorkerResult } from "./schemas.js";
import { type RoleSpec } from "./roles.js";
import { type DEFAULT_RETRY_POLICY } from "./config.js";

export type AgyRunOptions = {
    role: Exclude<RoleSpec["role"], "health">;
    goal: string;
    context?: string;
    workspace: string;
    extraConstraints?: string;
    timeoutMs?: number;
    highAutonomy?: boolean;
    addDirs?: string[];
    gate?: import("./concurrency.js").ConcurrencyGate;
    runId?: string;
    sessionKey?: string;
    sessionsPath?: string;
    logicalWorkspace?: string;
};
export type SpawnResult = {
    exitCode: number | null;
    signal: NodeJS.Signals | null;
    stdout: string;
    stderr: string;
    timedOut: boolean;
    argv: string[];
};
export declare function agyMissing(): boolean;
export declare function spawnAgy(args: string[], opts: {
    cwd: string;
    timeoutMs: number;
    env?: NodeJS.ProcessEnv;
}): Promise<SpawnResult>;
export declare function buildAgyArgs(input: AgyRunOptions, spec: RoleSpec): string[];
export declare function calculateBackoffMs(attempt: number, policy?: Partial<typeof DEFAULT_RETRY_POLICY>): number;
export declare function isTransientFailure(input: {
    err?: unknown;
    code?: string;
    stdout?: string;
    stderr?: string;
    exitCode?: number | null;
    timedOut?: boolean;
}): boolean;
export declare function getWorkspaceFingerprint(workspace: string): { type: string; state: string } | null;
export declare function wasWorkspaceModified(before: { type: string; state: string } | null, after: { type: string; state: string } | null): boolean;
export declare function runWorker(input: AgyRunOptions): Promise<WorkerResult>;
export declare function errorResult(role: WorkerResult["role"], err: unknown): WorkerResult;
