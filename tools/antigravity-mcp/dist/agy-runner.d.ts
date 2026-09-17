import { type WorkerResult } from "./schemas.js";
import { type RoleSpec } from "./roles.js";
export type AgyRunOptions = {
    role: Exclude<RoleSpec["role"], "health">;
    goal: string;
    context?: string;
    workspace: string;
    extraConstraints?: string;
    timeoutMs?: number;
    highAutonomy?: boolean;
    addDirs?: string[];
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
export declare function runWorker(input: AgyRunOptions): Promise<WorkerResult>;
export declare function errorResult(role: WorkerResult["role"], err: unknown): WorkerResult;
