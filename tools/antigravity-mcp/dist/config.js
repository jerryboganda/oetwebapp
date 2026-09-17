import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const DEFAULT_BUDGETS = {
    explore: 300_000,
    research: 300_000,
    review: 420_000,
    implement: 600_000,
    test: 600_000,
    debug: 600_000,
};
export const DEFAULT_ROLE_BUDGETS = DEFAULT_BUDGETS;

export const ROLE_POLICIES = {
    explore: {
        kind: "read",
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    research: {
        kind: "read",
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    review: {
        kind: "read",
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    implement: {
        kind: "write",
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: true,
    },
    test: {
        kind: "write",
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    debug: {
        kind: "write",
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: false,
    },
};

export const SAFETY_MARGIN_MS = 15_000;
export const MIN_PRINT_TIMEOUT_SECONDS = 10;

export const DEFAULT_RETRY_POLICY = {
    maxAttempts: 3,
    baseBackoffMs: 2000,
    backoffFactor: 2,
    maxJitterMs: 1000,
    hardCapTotalRetryMs: 600_000,
};

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const TOOL_ROOT = path.resolve(__dirname, "..");
export const CONFIG_FILE_PATH = path.join(TOOL_ROOT, "agy-bridge.config.json");

function readConfigOverrides() {
    try {
        if (!fs.existsSync(CONFIG_FILE_PATH)) {
            return null;
        }
    }
    catch {
        return null;
    }
    try {
        const raw = fs.readFileSync(CONFIG_FILE_PATH, "utf8");
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
            return parsed;
        }
        console.warn(`[agy-bridge] Warning: ${CONFIG_FILE_PATH} did not contain a JSON object. Using defaults.`);
        return null;
    }
    catch (err) {
        console.warn(`[agy-bridge] Warning: Malformed JSON in ${CONFIG_FILE_PATH}; falling back to defaults. Error: ${err instanceof Error ? err.message : String(err)}`);
        return null;
    }
}

export const CONFIG_OVERRIDES = readConfigOverrides();

function resolveBudgets(overrides) {
    const budgets = { ...DEFAULT_BUDGETS };
    if (!overrides) {
        return budgets;
    }
    const source = (overrides.budgets && typeof overrides.budgets === "object")
        ? overrides.budgets
        : (overrides.timeouts && typeof overrides.timeouts === "object")
            ? overrides.timeouts
            : (overrides.roles && typeof overrides.roles === "object")
                ? overrides.roles
                : overrides;
    for (const role of Object.keys(DEFAULT_BUDGETS)) {
        const candidate = source[role];
        let val;
        if (typeof candidate === "number") {
            val = candidate;
        }
        else if (candidate && typeof candidate === "object" && typeof candidate.timeoutMs === "number") {
            val = candidate.timeoutMs;
        }
        if (typeof val === "number" && Number.isFinite(val) && val > 0) {
            budgets[role] = Math.round(val);
        }
    }
    return budgets;
}

export const ROLE_BUDGETS = resolveBudgets(CONFIG_OVERRIDES);
export const budgets = ROLE_BUDGETS;

function resolvePolicies(overrides) {
    const policies = { ...ROLE_POLICIES };
    if (!overrides) {
        return policies;
    }
    const source = (overrides.policies && typeof overrides.policies === "object")
        ? overrides.policies
        : (overrides.roles && typeof overrides.roles === "object")
            ? overrides.roles
            : null;
    if (source) {
        for (const role of Object.keys(ROLE_POLICIES)) {
            const override = source[role];
            if (override && typeof override === "object") {
                policies[role] = {
                    ...policies[role],
                    ...(typeof override.kind === "string" ? { kind: override.kind } : {}),
                    ...(typeof override.mode === "string" ? { mode: override.mode } : {}),
                    ...(typeof override.sandbox === "boolean" ? { sandbox: override.sandbox } : {}),
                    ...(typeof override.highAutonomyOptIn === "boolean" ? { highAutonomyOptIn: override.highAutonomyOptIn } : {}),
                };
            }
        }
    }
    return policies;
}

export const RESOLVED_POLICIES = resolvePolicies(CONFIG_OVERRIDES);

export const ROLE_SPECS = Object.fromEntries(
    Object.keys(RESOLVED_POLICIES).map((role) => [
        role,
        {
            role,
            ...RESOLVED_POLICIES[role],
            timeoutMs: ROLE_BUDGETS[role] ?? DEFAULT_BUDGETS[role],
        },
    ])
);

export function computePrintTimeoutSeconds(budgetOrRole) {
    const budgetMs = typeof budgetOrRole === "number"
        ? budgetOrRole
        : (ROLE_BUDGETS[budgetOrRole] ?? DEFAULT_BUDGETS[budgetOrRole] ?? 300_000);
    const seconds = Math.floor((budgetMs - SAFETY_MARGIN_MS) / 1000);
    return Math.max(MIN_PRINT_TIMEOUT_SECONDS, seconds);
}

export const computePrintTimeout = computePrintTimeoutSeconds;

export function getRoleBudget(role) {
    return ROLE_BUDGETS[role] ?? DEFAULT_BUDGETS[role] ?? 300_000;
}

function resolveRetryPolicy(overrides) {
    const policy = { ...DEFAULT_RETRY_POLICY };
    if (!overrides?.retry || typeof overrides.retry !== "object") {
        return policy;
    }
    const r = overrides.retry;
    if (typeof r.maxAttempts === "number" && r.maxAttempts > 0) policy.maxAttempts = r.maxAttempts;
    if (typeof r.baseBackoffMs === "number" && r.baseBackoffMs > 0) policy.baseBackoffMs = r.baseBackoffMs;
    if (typeof r.backoffFactor === "number" && r.backoffFactor > 0) policy.backoffFactor = r.backoffFactor;
    if (typeof r.maxJitterMs === "number" && r.maxJitterMs >= 0) policy.maxJitterMs = r.maxJitterMs;
    if (typeof r.hardCapTotalRetryMs === "number" && r.hardCapTotalRetryMs > 0) policy.hardCapTotalRetryMs = r.hardCapTotalRetryMs;
    return policy;
}

export const RETRY_POLICY = resolveRetryPolicy(CONFIG_OVERRIDES);

export const DEFAULT_WORKTREE_CONFIG = {
    root: path.join(os.tmpdir(), "agy-worktrees"),
    maxAgeMs: 24 * 60 * 60 * 1000,
    gitTimeoutMs: 15_000,
};

function resolveWorktreeConfig(overrides) {
    const config = { ...DEFAULT_WORKTREE_CONFIG };
    if (process.env.AGY_WORKTREE_ROOT?.trim()) {
        config.root = path.resolve(process.env.AGY_WORKTREE_ROOT.trim());
    }
    if (process.env.AGY_WORKTREE_MAX_AGE_MS) {
        const parsed = Number(process.env.AGY_WORKTREE_MAX_AGE_MS);
        if (Number.isFinite(parsed) && parsed > 0) config.maxAgeMs = parsed;
    }
    const source = (overrides?.worktree && typeof overrides.worktree === "object") ? overrides.worktree : null;
    if (source) {
        if (typeof source.root === "string" && source.root.trim() && !process.env.AGY_WORKTREE_ROOT) {
            config.root = path.resolve(source.root.trim());
        }
        if (typeof source.maxAgeMs === "number" && source.maxAgeMs > 0 && !process.env.AGY_WORKTREE_MAX_AGE_MS) {
            config.maxAgeMs = source.maxAgeMs;
        }
        if (typeof source.gitTimeoutMs === "number" && source.gitTimeoutMs > 0) {
            config.gitTimeoutMs = source.gitTimeoutMs;
        }
    }
    return config;
}

export const WORKTREE_CONFIG = resolveWorktreeConfig(CONFIG_OVERRIDES);

export const DEFAULT_SESSION_CONFIG = {
    path: path.join(TOOL_ROOT, ".agy-state", "sessions.json"),
    maxSessions: 100,
    ttlMs: 7 * 24 * 60 * 60 * 1000,
};

function resolveSessionConfig(overrides) {
    const config = { ...DEFAULT_SESSION_CONFIG };
    if (process.env.AGY_SESSIONS_PATH?.trim()) {
        config.path = path.resolve(process.env.AGY_SESSIONS_PATH.trim());
    }
    if (process.env.AGY_SESSIONS_MAX) {
        const parsed = Number(process.env.AGY_SESSIONS_MAX);
        if (Number.isFinite(parsed) && parsed > 0) config.maxSessions = Math.round(parsed);
    }
    if (process.env.AGY_SESSIONS_TTL_MS) {
        const parsed = Number(process.env.AGY_SESSIONS_TTL_MS);
        if (Number.isFinite(parsed) && parsed > 0) config.ttlMs = Math.round(parsed);
    }
    const source = (overrides?.sessions && typeof overrides.sessions === "object") ? overrides.sessions : null;
    if (source) {
        if (typeof source.path === "string" && source.path.trim() && !process.env.AGY_SESSIONS_PATH) {
            config.path = path.resolve(source.path.trim());
        }
        if (typeof source.maxSessions === "number" && source.maxSessions > 0 && !process.env.AGY_SESSIONS_MAX) {
            config.maxSessions = Math.round(source.maxSessions);
        }
        if (typeof source.ttlMs === "number" && source.ttlMs > 0 && !process.env.AGY_SESSIONS_TTL_MS) {
            config.ttlMs = Math.round(source.ttlMs);
        }
    }
    return config;
}

export const SESSION_CONFIG = resolveSessionConfig(CONFIG_OVERRIDES);

