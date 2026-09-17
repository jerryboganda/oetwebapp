import type { WorkerRole } from "./schemas.js";
import type { RoleSpec } from "./roles.js";

export declare const DEFAULT_BUDGETS: Record<Exclude<WorkerRole, "health">, number>;
export declare const DEFAULT_ROLE_BUDGETS: Record<Exclude<WorkerRole, "health">, number>;
export declare const ROLE_POLICIES: Record<Exclude<WorkerRole, "health">, {
    kind: "read" | "write";
    mode: "plan" | "accept-edits";
    sandbox: boolean;
    highAutonomyOptIn: boolean;
}>;
export declare const SAFETY_MARGIN_MS: number;
export declare const MIN_PRINT_TIMEOUT_SECONDS: number;
export declare const DEFAULT_RETRY_POLICY: {
    maxAttempts: number;
    baseBackoffMs: number;
    backoffFactor: number;
    maxJitterMs: number;
    hardCapTotalRetryMs: number;
};
export declare const RETRY_POLICY: typeof DEFAULT_RETRY_POLICY;
export declare const TOOL_ROOT: string;
export declare const CONFIG_FILE_PATH: string;
export declare const CONFIG_OVERRIDES: Record<string, any> | null;
export declare const ROLE_BUDGETS: Record<Exclude<WorkerRole, "health">, number>;
export declare const budgets: Record<Exclude<WorkerRole, "health">, number>;
export declare const RESOLVED_POLICIES: typeof ROLE_POLICIES;
export declare const ROLE_SPECS: Record<Exclude<WorkerRole, "health">, RoleSpec>;
export declare function computePrintTimeoutSeconds(budgetOrRole: number | Exclude<WorkerRole, "health">): number;
export declare const computePrintTimeout: typeof computePrintTimeoutSeconds;
export declare function getRoleBudget(role: Exclude<WorkerRole, "health">): number;
export declare const DEFAULT_WORKTREE_CONFIG: {
    root: string;
    maxAgeMs: number;
    gitTimeoutMs: number;
};
export declare const WORKTREE_CONFIG: typeof DEFAULT_WORKTREE_CONFIG;
