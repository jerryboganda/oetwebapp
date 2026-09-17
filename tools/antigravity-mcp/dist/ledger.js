import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { CONFIG_OVERRIDES, TOOL_ROOT } from "./config.js";
import { extractAgyEnvelope, extractJsonObject } from "./schemas.js";

export const DEFAULT_LEDGER_CONFIG = {
    path: path.join(TOOL_ROOT, ".agy-state", "ledger.jsonl"),
    staleThresholdMs: 2 * 60 * 60 * 1000,
};

export function resolveLedgerConfig(overrides = CONFIG_OVERRIDES) {
    const config = { ...DEFAULT_LEDGER_CONFIG };
    if (process.env.AGY_LEDGER_PATH?.trim()) {
        config.path = path.resolve(process.env.AGY_LEDGER_PATH.trim());
    }
    if (process.env.AGY_LEDGER_STALE_THRESHOLD_MS) {
        const parsed = Number(process.env.AGY_LEDGER_STALE_THRESHOLD_MS);
        if (Number.isFinite(parsed) && parsed > 0) config.staleThresholdMs = Math.round(parsed);
    }
    const source = (overrides?.ledger && typeof overrides.ledger === "object") ? overrides.ledger : null;
    if (source) {
        if (typeof source.path === "string" && source.path.trim() && !process.env.AGY_LEDGER_PATH) {
            config.path = path.resolve(source.path.trim());
        }
        if (typeof source.staleThresholdMs === "number" && source.staleThresholdMs > 0 && !process.env.AGY_LEDGER_STALE_THRESHOLD_MS) {
            config.staleThresholdMs = Math.round(source.staleThresholdMs);
        }
    }
    return config;
}

export const LEDGER_CONFIG = resolveLedgerConfig(CONFIG_OVERRIDES);

/**
 * Generates a collision-resistant run ID.
 */
export function generateRunId() {
    if (typeof crypto.randomUUID === "function") {
        return `run_${crypto.randomUUID()}`;
    }
    return `run_${Date.now()}_${crypto.randomBytes(8).toString("hex")}`;
}

/**
 * Creates a standard running record for the ledger.
 */
export function createRunningRecord(opts = {}) {
    const startedAt = opts.startedAt || new Date().toISOString();
    return {
        runId: opts.runId || generateRunId(),
        sessionKey: opts.sessionKey ?? null,
        role: opts.role || "unknown",
        workspace: opts.workspace ? path.resolve(opts.workspace) : process.cwd(),
        worktreePath: opts.worktreePath ? path.resolve(opts.worktreePath) : null,
        model: opts.model || "gemini-3.8-flash-high",
        effort: opts.effort || "high",
        mode: opts.mode || "plan",
        attempt: typeof opts.attempt === "number" ? opts.attempt : 1,
        startedAt,
        endedAt: null,
        durationMs: null,
        status: "running",
        exitCode: null,
        errorCode: null,
        conversationId: opts.conversationId ?? opts.conversation_id ?? null,
        goal: opts.goal ? String(opts.goal).slice(0, 2000) : null,
        context: opts.context ? String(opts.context).slice(0, 1000) : null,
        usage: null,
    };
}

/**
 * Creates a terminal record from a running record and completion data.
 */
export function createTerminalRecord(runningRecord, completion = {}) {
    const endedAt = completion.endedAt || new Date().toISOString();
    const startedAtTime = runningRecord?.startedAt ? new Date(runningRecord.startedAt).getTime() : null;
    const endedAtTime = new Date(endedAt).getTime();
    const durationMs = completion.durationMs ?? (startedAtTime ? Math.max(0, endedAtTime - startedAtTime) : null);
    const usage = completion.usage ? (extractUsage(completion.usage) ?? completion.usage) : null;

    return {
        ...(runningRecord || {}),
        endedAt,
        durationMs,
        status: completion.status || "SUCCESS",
        exitCode: completion.exitCode ?? (completion.status === "SUCCESS" ? 0 : 1),
        errorCode: completion.errorCode ?? null,
        conversationId: completion.conversationId ?? completion.conversation_id ?? runningRecord?.conversationId ?? null,
        usage: usage ?? null,
    };
}

/**
 * Extracts token usage object from agy stdout string or envelope object.
 */
export function extractUsage(stdoutOrEnvelope) {
    if (!stdoutOrEnvelope) return null;
    if (typeof stdoutOrEnvelope === "object") {
        if (stdoutOrEnvelope.usage && typeof stdoutOrEnvelope.usage === "object") {
            return stdoutOrEnvelope.usage;
        }
        if (typeof stdoutOrEnvelope.total_tokens === "number") {
            return stdoutOrEnvelope;
        }
    }
    if (typeof stdoutOrEnvelope === "string") {
        const env = extractAgyEnvelope(stdoutOrEnvelope);
        if (env?.usage && typeof env.usage === "object") {
            return env.usage;
        }
        const obj = extractJsonObject(stdoutOrEnvelope);
        if (obj?.usage && typeof obj.usage === "object") {
            return obj.usage;
        }
        try {
            const parsed = JSON.parse(stdoutOrEnvelope);
            if (parsed?.usage && typeof parsed.usage === "object") {
                return parsed.usage;
            }
            if (typeof parsed?.total_tokens === "number") {
                return parsed;
            }
        } catch {
            // ignore
        }
    }
    return null;
}

/**
 * Appends a record to the append-only JSONL run ledger.
 * Reuses the crash-safe atomic-write idiom from dist/sessions.js:
 * writes full content to a temp file first, then performs an atomic rename,
 * with Windows fallback and direct append fallback.
 * Never throws into the caller: a ledger failure degrades to a logged warning.
 */
export function appendLedgerRecord(record, filePath = LEDGER_CONFIG.path) {
    try {
        const dir = path.dirname(filePath);
        try {
            if (!fs.existsSync(dir)) {
                fs.mkdirSync(dir, { recursive: true });
            }
        } catch (dirErr) {
            console.warn(`[agy-ledger] Warning: Failed to create directory ${dir}: ${dirErr instanceof Error ? dirErr.message : String(dirErr)}`);
        }

        const line = `${JSON.stringify(record)}\n`;
        const tempFile = path.join(dir, `.${path.basename(filePath)}.tmp.${Date.now()}.${Math.random().toString(36).slice(2, 7)}`);

        try {
            let existing = "";
            if (fs.existsSync(filePath)) {
                existing = fs.readFileSync(filePath, "utf8");
                if (existing.length > 0 && !existing.endsWith("\n")) {
                    existing += "\n";
                }
            }

            fs.writeFileSync(tempFile, existing + line, "utf8");
            try {
                fs.renameSync(tempFile, filePath);
            } catch (renameErr) {
                // Fallback for Windows file locking edge cases
                try {
                    fs.copyFileSync(tempFile, filePath);
                    fs.unlinkSync(tempFile);
                } catch {
                    try { fs.unlinkSync(tempFile); } catch {}
                    throw renameErr;
                }
            }
            return record;
        } catch (writeErr) {
            try { if (fs.existsSync(tempFile)) fs.unlinkSync(tempFile); } catch {}
            // Secondary fallback: direct append
            try {
                let prefix = "";
                if (fs.existsSync(filePath)) {
                    const content = fs.readFileSync(filePath, "utf8");
                    if (content.length > 0 && !content.endsWith("\n")) {
                        prefix = "\n";
                    }
                }
                fs.appendFileSync(filePath, prefix + line, "utf8");
                return record;
            } catch (appendErr) {
                console.warn(`[agy-ledger] Warning: Failed to write ledger record to ${filePath}: ${writeErr instanceof Error ? writeErr.message : String(writeErr)}`);
                return null;
            }
        }
    } catch (err) {
        console.warn(`[agy-ledger] Warning: Unexpected error appending ledger record: ${err instanceof Error ? err.message : String(err)}`);
        return null;
    }
}

export const appendRecord = appendLedgerRecord;

/**
 * Reads all records from the JSONL ledger file.
 * Tolerates missing files and malformed lines without throwing.
 */
export function readAllRecords(filePath = LEDGER_CONFIG.path) {
    try {
        if (!fs.existsSync(filePath)) {
            return [];
        }
        const content = fs.readFileSync(filePath, "utf8");
        const lines = content.split(/\r?\n/);
        const records = [];
        for (let i = 0; i < lines.length; i++) {
            const trimmed = lines[i].trim();
            if (!trimmed) continue;
            try {
                const parsed = JSON.parse(trimmed);
                if (parsed && typeof parsed === "object") {
                    records.push(parsed);
                }
            } catch (parseErr) {
                console.warn(`[agy-ledger] Warning: Skipping malformed ledger line ${i + 1} at ${filePath}: ${parseErr instanceof Error ? parseErr.message : String(parseErr)}`);
            }
        }
        return records;
    } catch (err) {
        console.warn(`[agy-ledger] Warning: Failed to read ledger from ${filePath}: ${err instanceof Error ? err.message : String(err)}`);
        return [];
    }
}

export const readLedgerRecords = readAllRecords;

/**
 * Reads recent records from the ledger, sorted newest first, with optional status/role/session filter and limit.
 */
export function readRecentRecords(options = {}, filePath = LEDGER_CONFIG.path) {
    try {
        const parsedLimit = Number(options.limit);
        const limit = Number.isFinite(parsedLimit) && parsedLimit > 0 ? parsedLimit : 50;
        const statusFilter = options.status ? String(options.status).trim() : null;
        const roleFilter = options.role ? String(options.role).trim() : null;
        const sessionKeyFilter = options.sessionKey ? String(options.sessionKey).trim() : null;

        const all = readAllRecords(filePath);
        // Reverse so that most recently appended records come first
        const reversed = all.slice().reverse();

        const filtered = [];
        for (const record of reversed) {
            if (statusFilter && record.status?.toLowerCase() !== statusFilter.toLowerCase()) {
                continue;
            }
            if (roleFilter && record.role !== roleFilter) {
                continue;
            }
            if (sessionKeyFilter && record.sessionKey !== sessionKeyFilter) {
                continue;
            }
            filtered.push(record);
            if (filtered.length >= limit) {
                break;
            }
        }
        return filtered;
    } catch (err) {
        console.warn(`[agy-ledger] Warning: Failed to read recent records: ${err instanceof Error ? err.message : String(err)}`);
        return [];
    }
}

export const getRecentRecords = readRecentRecords;

/**
 * Finds 'stale' running entries whose startedAt is older than a configurable threshold.
 * Checks whether a run's latest recorded state is still 'running' and has exceeded the threshold.
 */
export function findStaleRuns(options = {}, filePath = LEDGER_CONFIG.path) {
    try {
        const rawThreshold = options.thresholdMs ?? options.staleThresholdMs ?? options.olderThanMs ?? LEDGER_CONFIG.staleThresholdMs;
        const parsedThreshold = Number(rawThreshold);
        const thresholdMs = Number.isFinite(parsedThreshold) && parsedThreshold > 0
            ? parsedThreshold
            : (2 * 60 * 60 * 1000);
        const now = options.now ? new Date(options.now).getTime() : Date.now();
        const records = readAllRecords(filePath);

        // Find the latest record for each runId
        const latestByRunId = new Map();
        for (const r of records) {
            if (r.runId) {
                latestByRunId.set(r.runId, r);
            }
        }

        const stale = [];
        for (const [runId, latest] of latestByRunId.entries()) {
            if (String(latest.status).toLowerCase() === "running") {
                const started = new Date(latest.startedAt).getTime();
                if (Number.isFinite(started) && (now - started) > thresholdMs) {
                    stale.push({
                        ...latest,
                        ageMs: now - started,
                    });
                }
            }
        }

        return stale;
    } catch (err) {
        console.warn(`[agy-ledger] Warning: Failed to find stale runs: ${err instanceof Error ? err.message : String(err)}`);
        return [];
    }
}

export const getStaleRuns = findStaleRuns;

/**
 * Computes run counts by status and summed total tokens across records.
 */
export function getLedgerStats(options = {}, filePath = LEDGER_CONFIG.path) {
    try {
        const parsedLimit = Number(options.limit);
        const hasLimit = Number.isFinite(parsedLimit) && parsedLimit > 0;
        const records = hasLimit ? readRecentRecords({ limit: parsedLimit }, filePath) : readAllRecords(filePath);

        const countsByStatus = {};
        let totalTokens = 0;
        let totalRecords = 0;

        for (const record of records) {
            totalRecords++;
            const st = record.status || "unknown";
            countsByStatus[st] = (countsByStatus[st] || 0) + 1;

            const tokens = record.usage?.total_tokens ?? record.total_tokens ?? record.totalTokens ?? 0;
            if (typeof tokens === "number" && Number.isFinite(tokens)) {
                totalTokens += tokens;
            }
        }

        return {
            totalRecords,
            countsByStatus,
            totalTokens,
        };
    } catch (err) {
        console.warn(`[agy-ledger] Warning: Failed to compute ledger stats: ${err instanceof Error ? err.message : String(err)}`);
        return {
            totalRecords: 0,
            countsByStatus: {},
            totalTokens: 0,
        };
    }
}

export const getRunStats = getLedgerStats;
