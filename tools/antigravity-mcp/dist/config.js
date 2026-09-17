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

/**
 * DEFAULT ROLE ROUTING CONFIGURATION
 *
 * Defaults to EMPTY map meaning 'use the pinned model and effort' from security.js
 * (PINNED_MODEL = "gemini-3.8-flash-high", PINNED_EFFORT = "high").
 * An unconfigured bridge behaves exactly as today with zero behavioral drift.
 */
export const DEFAULT_ROLE_ROUTING = {};
export const DEFAULT_ROUTING = DEFAULT_ROLE_ROUTING;

/**
 * OPT-IN COST-OPTIMIZED ROUTING PRESET (EXAMPLE - NOT ACTIVE BY DEFAULT)
 *
 * Activating routing is a deliberate human choice. By default, all roles run on the
 * pinned model (gemini-3.8-flash-high) and effort (high) to ensure predictable, reproducible quality.
 *
 * Cost strategy:
 * - Read-only roles such as explore and research can use gemini-3.8-flash-low or gemini-3.8-flash-medium
 *   for cheap scanning, file discovery, and workspace reading.
 * - Mutating and high-rigor roles such as implement, debug, and review stay on gemini-3.8-flash-high.
 * - Note: gemini-3.1-pro-high is available for the hardest reasoning at higher cost.
 *
 * Activating any routing override or preset is a deliberate human choice.
 * To activate, configure in agy-bridge.config.json under "routing":
 *
 *   "routing": {
 *     "explore": { "model": "gemini-3.8-flash-low", "effort": "low" },
 *     "research": { "model": "gemini-3.8-flash-medium", "effort": "medium" }
 *   }
 */
export const EXAMPLE_ROUTING_PRESET = {
    explore: { model: "gemini-3.8-flash-low", effort: "low" },
    research: { model: "gemini-3.8-flash-medium", effort: "medium" },
    review: { model: "gemini-3.8-flash-high", effort: "high" },
    implement: { model: "gemini-3.8-flash-high", effort: "high" },
    test: { model: "gemini-3.8-flash-high", effort: "high" },
    debug: { model: "gemini-3.8-flash-high", effort: "high" },
};
// Note: gemini-3.1-pro-high is available for the hardest reasoning at higher cost.
export const EXAMPLE_ROLE_ROUTING = EXAMPLE_ROUTING_PRESET;

export function resolveRoleRouting(overrides) {
    const routing = {};
    if (!overrides || typeof overrides !== "object" || Array.isArray(overrides)) {
        return routing;
    }
    const source = (overrides.routing && typeof overrides.routing === "object" && !Array.isArray(overrides.routing))
        ? overrides.routing
        : (overrides.roleRouting && typeof overrides.roleRouting === "object" && !Array.isArray(overrides.roleRouting))
            ? overrides.roleRouting
            : (overrides.role_routing && typeof overrides.role_routing === "object" && !Array.isArray(overrides.role_routing))
                ? overrides.role_routing
                : (overrides.roles && typeof overrides.roles === "object" && !Array.isArray(overrides.roles))
                    ? overrides.roles
                    : overrides;

    if (!source || typeof source !== "object" || Array.isArray(source)) {
        return routing;
    }

    for (const [role, entry] of Object.entries(source)) {
        if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
            continue;
        }
        const item = {};
        if (typeof entry.model === "string" && entry.model.trim()) {
            item.model = entry.model.trim();
        }
        if (typeof entry.effort === "string" && entry.effort.trim()) {
            item.effort = entry.effort.trim();
        }
        if (Object.keys(item).length > 0) {
            routing[role] = item;
        }
    }
    return routing;
}

export const resolveRouting = resolveRoleRouting;
export const ROLE_ROUTING = resolveRoleRouting(CONFIG_OVERRIDES);
export const routing = ROLE_ROUTING;
export const roleRouting = ROLE_ROUTING;

export function getRoleRouting(role, routingConfig = ROLE_ROUTING) {
    if (!routingConfig || typeof routingConfig !== "object" || Array.isArray(routingConfig)) {
        return undefined;
    }
    return routingConfig[role];
}

export const ROLE_SPECS = Object.fromEntries(
    Object.keys(RESOLVED_POLICIES).map((role) => [
        role,
        {
            role,
            ...RESOLVED_POLICIES[role],
            timeoutMs: ROLE_BUDGETS[role] ?? DEFAULT_BUDGETS[role],
            ...(ROLE_ROUTING[role]?.model ? { model: ROLE_ROUTING[role].model } : {}),
            ...(ROLE_ROUTING[role]?.effort ? { effort: ROLE_ROUTING[role].effort } : {}),
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

export const DEFAULT_BUDGET_CEILINGS = {
    maxAttempts: 3,
    maxWallClockMs: 1_800_000,
    maxTotalTokens: 1_000_000,
    maxRunsPerSession: 50,
};

export function resolveBudgetCeilings(overrides) {
    const ceilings = { ...DEFAULT_BUDGET_CEILINGS };
    if (process.env.AGY_MAX_ATTEMPTS) {
        const parsed = Number(process.env.AGY_MAX_ATTEMPTS);
        if (Number.isFinite(parsed) && parsed > 0) ceilings.maxAttempts = Math.round(parsed);
    }
    if (process.env.AGY_MAX_WALL_CLOCK_MS) {
        const parsed = Number(process.env.AGY_MAX_WALL_CLOCK_MS);
        if (Number.isFinite(parsed) && parsed > 0) ceilings.maxWallClockMs = Math.round(parsed);
    }
    if (process.env.AGY_MAX_TOTAL_TOKENS) {
        const parsed = Number(process.env.AGY_MAX_TOTAL_TOKENS);
        if (Number.isFinite(parsed) && parsed > 0) ceilings.maxTotalTokens = Math.round(parsed);
    }
    if (process.env.AGY_MAX_RUNS_PER_SESSION) {
        const parsed = Number(process.env.AGY_MAX_RUNS_PER_SESSION);
        if (Number.isFinite(parsed) && parsed > 0) ceilings.maxRunsPerSession = Math.round(parsed);
    }
    const source = (overrides?.budgetCeilings && typeof overrides.budgetCeilings === "object")
        ? overrides.budgetCeilings
        : (overrides?.ceilings && typeof overrides.ceilings === "object")
            ? overrides.ceilings
            : (overrides?.budget_ceilings && typeof overrides.budget_ceilings === "object")
                ? overrides.budget_ceilings
                : (overrides?.budget && typeof overrides.budget === "object" && !overrides.budget.explore)
                    ? overrides.budget
                    : null;
    if (source) {
        if (typeof source.maxAttempts === "number" && source.maxAttempts > 0 && !process.env.AGY_MAX_ATTEMPTS) {
            ceilings.maxAttempts = Math.round(source.maxAttempts);
        }
        if (typeof source.maxWallClockMs === "number" && source.maxWallClockMs > 0 && !process.env.AGY_MAX_WALL_CLOCK_MS) {
            ceilings.maxWallClockMs = Math.round(source.maxWallClockMs);
        }
        if (typeof source.maxTotalTokens === "number" && source.maxTotalTokens > 0 && !process.env.AGY_MAX_TOTAL_TOKENS) {
            ceilings.maxTotalTokens = Math.round(source.maxTotalTokens);
        }
        if (typeof source.maxRunsPerSession === "number" && source.maxRunsPerSession > 0 && !process.env.AGY_MAX_RUNS_PER_SESSION) {
            ceilings.maxRunsPerSession = Math.round(source.maxRunsPerSession);
        }
    }
    return ceilings;
}

export const BUDGET_CEILINGS = resolveBudgetCeilings(CONFIG_OVERRIDES);
export const budgetCeilings = BUDGET_CEILINGS;

export function getBudgetCeilings() {
    return BUDGET_CEILINGS;
}

