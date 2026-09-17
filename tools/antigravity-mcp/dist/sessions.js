import fs from "node:fs";
import path from "node:path";
import { SESSION_CONFIG } from "./config.js";
import { extractConversationId } from "./schemas.js";
import { PINNED_EFFORT, PINNED_MODEL } from "./security.js";

function createEmptyStore() {
    return {
        version: 1,
        sessions: {},
    };
}

/**
 * Loads the session store from disk.
 * Crash-safe: tolerates missing or corrupt file by falling back to empty state with a warning.
 * Never throws at import or call time.
 */
export function loadSessionStore(filePath = SESSION_CONFIG.path) {
    try {
        if (!fs.existsSync(filePath)) {
            return createEmptyStore();
        }
    } catch {
        return createEmptyStore();
    }

    try {
        const raw = fs.readFileSync(filePath, "utf8");
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
            console.warn(`[agy-session] Warning: ${filePath} did not contain a valid JSON object. Falling back to empty state.`);
            return createEmptyStore();
        }

        const store = {
            version: typeof parsed.version === "number" ? parsed.version : 1,
            sessions: (parsed.sessions && typeof parsed.sessions === "object" && !Array.isArray(parsed.sessions))
                ? parsed.sessions
                : {},
        };

        return pruneSessions(store, { ttlMs: SESSION_CONFIG.ttlMs, maxSessions: SESSION_CONFIG.maxSessions });
    } catch (err) {
        console.warn(`[agy-session] Warning: Corrupt or unreadable session store at ${filePath}; falling back to empty state. Error: ${err instanceof Error ? err.message : String(err)}`);
        return createEmptyStore();
    }
}

/**
 * Prunes expired sessions (TTL) and evicts least-recently-used sessions when exceeding maxSessions.
 */
export function pruneSessions(store, options = {}) {
    if (!store || typeof store !== "object" || !store.sessions || typeof store.sessions !== "object") {
        return store;
    }

    const ttlMs = options.ttlMs ?? SESSION_CONFIG.ttlMs ?? (7 * 24 * 60 * 60 * 1000);
    const maxSessions = options.maxSessions ?? SESSION_CONFIG.maxSessions ?? 100;
    const now = Date.now();

    // 1. Drop entries whose age exceeds TTL
    for (const [key, session] of Object.entries(store.sessions)) {
        if (!session || typeof session !== "object") {
            delete store.sessions[key];
            continue;
        }
        const time = new Date(session.timestamp).getTime();
        if (Number.isFinite(time) && (now - time) > ttlMs) {
            delete store.sessions[key];
        }
    }

    // 2. If store exceeds maxSessions, evict least-recently-used (oldest timestamp first)
    const entries = Object.entries(store.sessions);
    if (entries.length > maxSessions) {
        entries.sort((a, b) => {
            const timeA = new Date(a[1]?.timestamp).getTime() || 0;
            const timeB = new Date(b[1]?.timestamp).getTime() || 0;
            return timeA - timeB; // ascending: oldest first
        });

        const evictCount = entries.length - maxSessions;
        for (let i = 0; i < evictCount; i++) {
            delete store.sessions[entries[i][0]];
        }
    }

    return store;
}

/**
 * Crash-safe save: writes to a temp file first, then performs an atomic rename.
 */
export function saveSessionStore(store, filePath = SESSION_CONFIG.path, options = {}) {
    pruneSessions(store, options);

    const dir = path.dirname(filePath);
    try {
        if (!fs.existsSync(dir)) {
            fs.mkdirSync(dir, { recursive: true });
        }
    } catch (err) {
        console.warn(`[agy-session] Warning: Failed to create directory ${dir}: ${err instanceof Error ? err.message : String(err)}`);
    }

    const tempFile = path.join(dir, `.${path.basename(filePath)}.tmp.${Date.now()}.${Math.random().toString(36).slice(2, 7)}`);
    const json = `${JSON.stringify(store, null, 2)}\n`;

    try {
        fs.writeFileSync(tempFile, json, "utf8");
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
    } catch (writeErr) {
        try { if (fs.existsSync(tempFile)) fs.unlinkSync(tempFile); } catch {}
        console.warn(`[agy-session] Warning: Failed to write session store to ${filePath}: ${writeErr instanceof Error ? writeErr.message : String(writeErr)}`);
    }
}

/**
 * Retrieves a single session by key.
 */
export function getSession(sessionKey, filePath = SESSION_CONFIG.path) {
    if (!sessionKey || typeof sessionKey !== "string") {
        return null;
    }
    const store = loadSessionStore(filePath);
    return store.sessions[sessionKey] ?? null;
}

/**
 * Saves or updates a session entry.
 */
export function saveSession(sessionData, filePath = SESSION_CONFIG.path, options = {}) {
    const { sessionKey, conversationId, role, workspace } = sessionData;
    if (!sessionKey || !conversationId) {
        return null;
    }

    const store = loadSessionStore(filePath);
    const existing = store.sessions[sessionKey];

    let turns = 1;
    if (typeof sessionData.turns === "number" && Number.isFinite(sessionData.turns)) {
        turns = sessionData.turns;
    } else if (existing && existing.conversationId === conversationId && existing.role === role && existing.workspace === path.resolve(workspace)) {
        turns = (existing.turns || 1) + 1;
    }

    const entry = {
        conversationId,
        sessionKey,
        role,
        workspace: path.resolve(workspace),
        model: sessionData.model || PINNED_MODEL,
        effort: sessionData.effort || PINNED_EFFORT,
        timestamp: sessionData.timestamp || new Date().toISOString(),
        turns,
        lastStatus: sessionData.lastStatus || "SUCCESS",
    };

    store.sessions[sessionKey] = entry;
    saveSessionStore(store, filePath, options);
    return entry;
}

/**
 * Extracts conversation id from agy envelope and persists the session.
 */
export function recordSessionFromEnvelope(opts, filePath = SESSION_CONFIG.path, options = {}) {
    const { sessionKey, envelope, role, workspace, model, effort, lastStatus } = opts;
    if (!sessionKey || !envelope) {
        return null;
    }

    const conversationId = extractConversationId(envelope);
    if (!conversationId) {
        return null;
    }

    const status = lastStatus || (typeof envelope === "object" && envelope?.status ? envelope.status : "SUCCESS");

    return saveSession({
        sessionKey,
        conversationId,
        role,
        workspace,
        model,
        effort,
        lastStatus: status,
    }, filePath, options);
}

/**
 * Checks whether a session can be resumed.
 * Strictly enforces workspace and role match. Logs any mismatch decision.
 */
export function findResumableConversation(opts, filePath = SESSION_CONFIG.path) {
    const { sessionKey, role, workspace } = opts;
    if (!sessionKey) {
        return null;
    }

    const session = getSession(sessionKey, filePath);
    if (!session || !session.conversationId) {
        return null;
    }

    const storedWorkspace = path.resolve(session.workspace);
    const requestedWorkspace = path.resolve(workspace);
    const roleMatches = session.role === role;
    const workspaceMatches = storedWorkspace === requestedWorkspace;

    if (!roleMatches || !workspaceMatches) {
        const reasons = [];
        if (!roleMatches) reasons.push(`role mismatch (stored: '${session.role}', requested: '${role}')`);
        if (!workspaceMatches) reasons.push(`workspace mismatch (stored: '${storedWorkspace}', requested: '${requestedWorkspace}')`);
        console.warn(`[agy-session] Session '${sessionKey}' cannot be resumed due to ${reasons.join(" and ")}. Starting a fresh conversation.`);
        return null;
    }

    return session.conversationId;
}

/**
 * Lists all stored sessions without id truncation.
 */
export function listSessions(filePath = SESSION_CONFIG.path) {
    const store = loadSessionStore(filePath);
    return Object.values(store.sessions);
}

/**
 * Clears one or all sessions.
 */
export function clearSessions(sessionKey, filePath = SESSION_CONFIG.path) {
    const store = loadSessionStore(filePath);
    if (sessionKey) {
        const existed = Boolean(store.sessions[sessionKey]);
        delete store.sessions[sessionKey];
        saveSessionStore(store, filePath);
        return {
            clearedCount: existed ? 1 : 0,
            remainingCount: Object.keys(store.sessions).length,
        };
    }

    const clearedCount = Object.keys(store.sessions).length;
    store.sessions = {};
    saveSessionStore(store, filePath);
    return {
        clearedCount,
        remainingCount: 0,
    };
}
