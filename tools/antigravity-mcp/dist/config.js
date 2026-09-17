import fs from "node:fs";
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
