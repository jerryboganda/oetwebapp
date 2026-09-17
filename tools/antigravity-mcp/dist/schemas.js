import * as z from "zod/v4";
export const WORKER_ROLES = [
    "explore",
    "research",
    "implement",
    "test",
    "debug",
    "review",
    "health",
];
export const workerResultSchema = z.object({
    status: z.enum(["SUCCESS", "PARTIAL", "BLOCKED", "ERROR"]),
    role: z.enum(WORKER_ROLES),
    summary: z.string(),
    evidence: z.array(z.object({
        path: z.string(),
        lineOrSymbol: z.string().optional(),
        finding: z.string(),
    })),
    filesRead: z.array(z.string()),
    filesChanged: z.array(z.string()),
    commandsRun: z.array(z.object({
        command: z.string(),
        exitCode: z.number().int(),
        result: z.string(),
    })),
    tests: z.array(z.object({
        name: z.string(),
        status: z.enum(["PASS", "FAIL", "NOT_RUN"]),
        evidence: z.string(),
    })),
    risks: z.array(z.string()),
    blockers: z.array(z.string()),
    recommendedNextStep: z.string(),
    confidence: z.enum(["high", "medium", "low"]),
    worktreePath: z.string().optional(),
});
export function emptyResult(role, patch) {
    return {
        role,
        evidence: [],
        filesRead: [],
        filesChanged: [],
        commandsRun: [],
        tests: [],
        risks: [],
        blockers: [],
        recommendedNextStep: "Inspect the error and retry or fall back to MAI.",
        confidence: "low",
        ...patch,
    };
}
export function unwrapAgyPayload(text) {
    const raw = extractJsonObject(text);
    if (!raw || typeof raw !== "object")
        return raw;
    const obj = raw;
    if ("filesRead" in obj && "summary" in obj && "role" in obj)
        return obj;
    if ("conversation_id" in obj && "response" in obj) {
        if (typeof obj.response === "string" && obj.response.trim()) {
            const inner = extractJsonObject(obj.response);
            if (inner)
                return inner;
        }
        if (obj.response && typeof obj.response === "object")
            return obj.response;
    }
    return raw;
}
export function extractAgyEnvelope(text) {
    if (!text)
        return null;
    if (typeof text === "object" && !Array.isArray(text)) {
        if ("conversation_id" in text && typeof text.conversation_id === "string") {
            return text;
        }
        return null;
    }
    if (typeof text !== "string")
        return null;
    const raw = extractJsonObject(text);
    if (!raw || typeof raw !== "object" || Array.isArray(raw))
        return null;
    if ("conversation_id" in raw && typeof raw.conversation_id === "string") {
        return raw;
    }
    return null;
}
export function extractConversationId(text) {
    const env = extractAgyEnvelope(text);
    return env?.conversation_id ?? null;
}
export function extractJsonObject(text) {
    const trimmed = text.trim();
    if (!trimmed)
        return null;
    try {
        return JSON.parse(trimmed);
    }
    catch {
        /* continue */
    }
    const first = trimmed.indexOf("{");
    const last = trimmed.lastIndexOf("}");
    if (first >= 0 && last > first) {
        try {
            return JSON.parse(trimmed.slice(first, last + 1));
        }
        catch {
            /* continue */
        }
    }
    const fence = trimmed.match(/```(?:json)?\s*([\s\S]*?)```/i);
    if (fence?.[1]) {
        try {
            return JSON.parse(fence[1].trim());
        }
        catch {
            return null;
        }
    }
    const candidates = scanTopLevelObjects(trimmed);
    for (let i = candidates.length - 1; i >= 0; i--) {
        try {
            const parsed = JSON.parse(candidates[i]);
            if (parsed && typeof parsed === "object" && "status" in parsed)
                return parsed;
        }
        catch {
            /* try the next candidate */
        }
    }
    for (let i = candidates.length - 1; i >= 0; i--) {
        try {
            const parsed = JSON.parse(candidates[i]);
            if (parsed && typeof parsed === "object")
                return parsed;
        }
        catch {
            /* try the next candidate */
        }
    }
    return null;
}


/**
 * Scans for balanced TOP-LEVEL JSON objects in a string.
 *
 * Needed because agy sometimes emits its result more than once in one response. The naive
 * first-brace-to-last-brace slice spans both objects, yields invalid JSON, and silently
 * downgraded valid worker results to PARTIAL with empty evidence arrays.
 * Brace matching is string- and escape-aware, so braces inside string literals are ignored.
 */
export function scanTopLevelObjects(text) {
    const out = [];
    if (typeof text !== "string" || !text)
        return out;
    const BACKSLASH = String.fromCharCode(92);
    const DQUOTE = String.fromCharCode(34);
    let depth = 0;
    let start = -1;
    let inString = false;
    let escaped = false;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (inString) {
            if (escaped)
                escaped = false;
            else if (c === BACKSLASH)
                escaped = true;
            else if (c === DQUOTE)
                inString = false;
            continue;
        }
        if (c === DQUOTE) {
            inString = true;
            continue;
        }
        if (c === "{") {
            if (depth === 0)
                start = i;
            depth++;
            continue;
        }
        if (c === "}") {
            if (depth > 0) {
                depth--;
                if (depth === 0 && start >= 0) {
                    out.push(text.slice(start, i + 1));
                    start = -1;
                }
            }
        }
    }
    return out;
}


export function parseWorkerResult(role, stdout) {
    const raw = unwrapAgyPayload(stdout);
    if (raw == null) {
        throw new Error("Worker stdout did not contain a JSON object");
    }
    const parsed = workerResultSchema.safeParse(raw);
    if (parsed.success) {
        return parsed.data.role === role ? parsed.data : { ...parsed.data, role };
    }
    if (raw && typeof raw === "object" && "conversation_id" in raw) {
        const env = raw;
        const denied = Array.isArray(env.denied_actions) ? env.denied_actions : [];
        return emptyResult(role, {
            status: env.status === "SUCCESS" ? "PARTIAL" : "ERROR",
            summary: typeof env.response === "string" && env.response.trim()
                ? env.response.trim()
                : `agy envelope without worker JSON (${denied.length} denied actions)`,
            risks: denied.length ? [JSON.stringify(denied).slice(0, 1000)] : [],
            recommendedNextStep: "Retry with a smaller read-only goal, or inspect denied_actions.",
            confidence: "low",
        });
    }
    throw new Error(parsed.error.message);
}
