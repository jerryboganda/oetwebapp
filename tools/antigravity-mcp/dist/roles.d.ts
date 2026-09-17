import type { WorkerRole } from "./schemas.js";
export type RoleSpec = {
    role: WorkerRole;
    kind: "read" | "write" | "worktree";
    timeoutMs: number;
    mode: "plan" | "accept-edits";
    sandbox: boolean;
    highAutonomyOptIn: boolean;
};
export declare const ROLE_SPECS: Record<Exclude<WorkerRole, "health">, RoleSpec>;
export declare function rolePrompt(input: {
    role: Exclude<WorkerRole, "health">;
    goal: string;
    context?: string;
    workspace: string;
    extraConstraints?: string;
}): string;
