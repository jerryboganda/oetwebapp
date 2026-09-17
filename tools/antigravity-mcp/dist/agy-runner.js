import { execFileSync, spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { BridgeError, classifyAgyFailure } from "./errors.js";
import { emptyResult, extractAgyEnvelope, extractJsonObject, parseWorkerResult } from "./schemas.js";
import { PINNED_EFFORT, PINNED_MODEL, ensureUseG1CreditsFalse, highAutonomyAllowed, logsDir, } from "./security.js";
import { ROLE_SPECS, rolePrompt } from "./roles.js";
import { BUDGET_CEILINGS, computePrintTimeoutSeconds, DEFAULT_BUDGET_CEILINGS, DEFAULT_RETRY_POLICY, RETRY_POLICY, ROLE_ROUTING, resolveRoleRouting, getRoleRouting } from "./config.js";
import { defaultGate } from "./concurrency.js";
import { createWorktree, cleanupStaleWorktrees } from "./worktree.js";
import { findResumableConversation, getSession, recordSessionFromEnvelope } from "./sessions.js";
import { applyReviewGate } from "./reviewgate.js";

function resolveAgyBin() {
    if (process.env.AGY_BIN?.trim())
        return process.env.AGY_BIN.trim();
    const home = os.homedir();
    const candidates = [
        path.join(home, "AppData", "Local", "agy", "bin", "agy.exe"),
        path.join(home, ".local", "bin", "agy"),
        "agy",
    ];
    for (const c of candidates) {
        if (c === "agy")
            return c;
        if (fs.existsSync(c))
            return c;
    }
    return "agy";
}

function resolveAgySpawn() {
    const bin = resolveAgyBin();
    if (/\.(mjs|cjs|js)$/i.test(bin)) {
        return { bin: process.execPath, prefixArgs: [bin] };
    }
    return { bin, prefixArgs: [] };
}

export function agyMissing() {
    const bin = resolveAgyBin();
    if (bin === "agy")
        return false;
    return !fs.existsSync(bin);
}

function faultInject() {
    const raw = process.env.AGY_FAULT_INJECT?.trim();
    if (!raw)
        return null;
    return raw;
}

export async function spawnAgy(args, opts) {
    ensureUseG1CreditsFalse();
    const { bin, prefixArgs } = resolveAgySpawn();
    const argv = [...prefixArgs, ...args];
    return new Promise((resolve, reject) => {
        const child = spawn(bin, argv, {
            cwd: opts.cwd,
            env: { ...process.env, ...opts.env },
            windowsHide: true,
            stdio: ["ignore", "pipe", "pipe"],
        });
        let stdout = "";
        let stderr = "";
        let timedOut = false;
        const timer = setTimeout(() => {
            timedOut = true;
            child.kill("SIGTERM");
        }, opts.timeoutMs);
        child.stdout?.on("data", (chunk) => {
            stdout += chunk.toString("utf8");
        });
        child.stderr?.on("data", (chunk) => {
            stderr += chunk.toString("utf8");
        });
        child.on("error", (err) => {
            clearTimeout(timer);
            const missing = err.code === "ENOENT" || agyMissing();
            reject(new BridgeError(missing ? "AGY_NOT_INSTALLED" : "INTERNAL_BRIDGE_ERROR", missing ? "agy executable not found" : err.message));
        });
        child.on("close", (exitCode, signal) => {
            clearTimeout(timer);
            ensureUseG1CreditsFalse();
            resolve({ exitCode, signal, stdout, stderr, timedOut, argv: [bin, ...argv] });
        });
    });
}

function writeLog(name, body) {
    const file = path.join(logsDir(), `${Date.now()}-${name}.json`);
    fs.writeFileSync(file, `${JSON.stringify(body, null, 2)}\n`, "utf8");
    return file;
}

export function resolveRoleModelAndEffort(role, options = {}) {
    let routingMap = null;
    let explicitModel = null;
    let explicitEffort = null;

    if (options && typeof options === "object") {
        if (options.routingOverride && typeof options.routingOverride === "object" && !Array.isArray(options.routingOverride)) {
            routingMap = options.routingOverride;
        } else if (options.routing && typeof options.routing === "object" && !Array.isArray(options.routing)) {
            routingMap = options.routing;
        } else if (options.input?.routing && typeof options.input.routing === "object" && !Array.isArray(options.input.routing)) {
            routingMap = options.input.routing;
        } else if (options.spec?.routing && typeof options.spec.routing === "object" && !Array.isArray(options.spec.routing)) {
            routingMap = options.spec.routing;
        } else if (!options.input && !options.spec && !options.routingOverride && !Array.isArray(options)) {
            routingMap = options;
        }

        if (typeof options.model === "string" && options.model.trim()) {
            explicitModel = options.model.trim();
        } else if (typeof options.input?.model === "string" && options.input.model.trim()) {
            explicitModel = options.input.model.trim();
        } else if (typeof options.spec?.model === "string" && options.spec.model.trim()) {
            explicitModel = options.spec.model.trim();
        }

        if (typeof options.effort === "string" && options.effort.trim()) {
            explicitEffort = options.effort.trim();
        } else if (typeof options.input?.effort === "string" && options.input.effort.trim()) {
            explicitEffort = options.input.effort.trim();
        } else if (typeof options.spec?.effort === "string" && options.spec.effort.trim()) {
            explicitEffort = options.spec.effort.trim();
        }
    }

    let roleEntry = null;
    if (routingMap && typeof routingMap === "object" && !Array.isArray(routingMap)) {
        const candidate = routingMap[role];
        if (candidate && typeof candidate === "object" && !Array.isArray(candidate)) {
            roleEntry = candidate;
        }
    }

    if (!roleEntry && ROLE_ROUTING && typeof ROLE_ROUTING === "object" && !Array.isArray(ROLE_ROUTING)) {
        const globalCandidate = ROLE_ROUTING[role];
        if (globalCandidate && typeof globalCandidate === "object" && !Array.isArray(globalCandidate)) {
            roleEntry = globalCandidate;
        }
    }

    const resolvedModel = (roleEntry && typeof roleEntry.model === "string" && roleEntry.model.trim())
        ? roleEntry.model.trim()
        : (explicitModel || PINNED_MODEL);

    const resolvedEffort = (roleEntry && typeof roleEntry.effort === "string" && roleEntry.effort.trim())
        ? roleEntry.effort.trim()
        : (explicitEffort || PINNED_EFFORT);

    const model = (typeof resolvedModel === "string" && resolvedModel.trim()) ? resolvedModel.trim() : PINNED_MODEL;
    const effort = (typeof resolvedEffort === "string" && resolvedEffort.trim()) ? resolvedEffort.trim() : PINNED_EFFORT;

    return { model, effort };
}

export function buildAgyArgs(input, spec, routingOverride) {
    const { model, effort } = resolveRoleModelAndEffort(input.role, {
        input,
        spec,
        routingOverride,
    });
    const prompt = rolePrompt({
        role: input.role,
        goal: input.goal,
        context: input.context,
        workspace: input.workspace,
        extraConstraints: input.extraConstraints,
        model,
        effort,
    });
    const timeout = input.timeoutMs ?? spec?.timeoutMs;
    const schemaFile = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../schemas/worker-result.schema.json");
    const args = [
        "-p",
        prompt,
        "--model",
        model,
        "--effort",
        effort,
        "--output-format",
        "json",
        "--json-schema",
        schemaFile,
        "--print-timeout",
        `${computePrintTimeoutSeconds(timeout)}s`,
        "--mode",
        spec?.mode ?? "plan",
    ];
    if (spec?.sandbox)
        args.push("--sandbox");
    if (input.sessionKey) {
        const conversationId = findResumableConversation({
            sessionKey: input.sessionKey,
            role: input.role,
            workspace: input.logicalWorkspace ?? input.workspace,
        }, input.sessionsPath);
        if (conversationId) {
            args.push("--conversation", conversationId);
        }
    }
    args.push("--add-dir", input.workspace);
    for (const dir of input.addDirs ?? [])
        args.push("--add-dir", dir);
    const disposable = highAutonomyAllowed(input.workspace);
    if (disposable) {
        args.push("--dangerously-skip-permissions");
    }
    return args;
}

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

export function calculateBackoffMs(attempt, policy = RETRY_POLICY || DEFAULT_RETRY_POLICY) {
    const base = policy.baseBackoffMs ?? 2000;
    const factor = policy.backoffFactor ?? 2;
    const maxJitter = policy.maxJitterMs ?? 1000;
    const exp = Math.max(0, attempt - 1);
    const exponential = base * Math.pow(factor, exp);
    const jitter = Math.floor(Math.random() * (maxJitter + 1));
    return exponential + jitter;
}

export function isTransientFailure(input) {
    const code = input.code || (input.err instanceof BridgeError ? input.err.code : "");
    const details = input.err instanceof BridgeError ? input.err.details || "" : "";
    const message = input.err instanceof Error ? input.err.message : String(input.err || "");
    const stdout = input.stdout || "";
    const stderr = input.stderr || "";
    const blob = `${stdout}\n${stderr}\n${message}\n${details}`.toLowerCase();

    // Deterministic exclusions: NEVER retry these
    if (code === "AUTH_REQUIRED" || /not logged in|login required|auth(entication)? required|unauthenticated|sign[- ]?in|oauth/.test(blob)) {
        return false;
    }
    if (code === "AGY_NOT_INSTALLED" || /agy executable not found|\benoent\b/.test(blob)) {
        return false;
    }
    if (code === "PERMISSION_DENIED" || /permission denied|access denied|not allowed|sandbox violation/.test(blob)) {
        return false;
    }
    if (code === "CANCELED") {
        return false;
    }
    if (/invalid[- ]?argument|bad request|\b400\b/.test(blob)) {
        return false;
    }

    // Transient failure matches:
    // 1. Quota & Rate limits (HTTP 429, RESOURCE_EXHAUSTED, quota, rate-limit)
    if (code === "RATE_LIMITED" || code === "QUOTA_EXHAUSTED") {
        return true;
    }
    if (/\b429\b|resource_exhausted|\bquota\b|rate[- ]?limit|too many requests|usage limit|exceeded.*quota/.test(blob)) {
        return true;
    }

    // 2. Network socket errors (ECONNRESET, ETIMEDOUT, EAI_AGAIN, socket hang up)
    if (/econnreset|etimedout|eai_again|socket hang up|connection reset|connection refused|network error|socket closed/.test(blob)) {
        return true;
    }

    // 3. 5xx Server errors (500, 502, 503, 504, bad gateway, service unavailable)
    if (code === "SERVICE_UNAVAILABLE") {
        return true;
    }
    if (/\b5\d{2}\b|bad gateway|service unavailable|gateway timeout|temporarily unavailable|internal server error/.test(blob)) {
        return true;
    }

    // 4. Timeout
    if (code === "TIMEOUT" || input.timedOut || /timed? ?out/.test(blob)) {
        return true;
    }

    // 5. Empty output with no parsable result
    const trimmedStdout = stdout.trim();
    if (!trimmedStdout) {
        return true;
    }

    return false;
}

export function getWorkspaceFingerprint(workspace) {
    try {
        const out = execFileSync("git", ["status", "--porcelain", "."], {
            cwd: workspace,
            timeout: 5000,
            stdio: ["ignore", "pipe", "ignore"],
            windowsHide: true,
        });
        return { type: "git", state: out.toString("utf8") };
    }
    catch {
        // Not a git repository or git binary failed
    }

    try {
        const entries = [];
        function scan(dir, depth) {
            if (depth > 4 || entries.length > 500)
                return;
            const items = fs.readdirSync(dir, { withFileTypes: true });
            for (const item of items) {
                if (item.name === "node_modules" || item.name === ".git" || item.name === ".tools-state")
                    continue;
                const full = path.join(dir, item.name);
                try {
                    const stat = fs.statSync(full);
                    entries.push(`${path.relative(workspace, full)}:${stat.size}:${stat.mtimeMs}`);
                    if (item.isDirectory()) {
                        scan(full, depth + 1);
                    }
                }
                catch {
                    /* ignore read errors */
                }
            }
        }
        scan(workspace, 0);
        return { type: "fs", state: entries.sort().join("\n") };
    }
    catch {
        return null;
    }
}

export function wasWorkspaceModified(before, after) {
    if (!before || !after) {
        return true;
    }
    if (before.type !== after.type) {
        return true;
    }
    return before.state !== after.state;
}

export function extractTokensFromStdout(stdout) {
    if (!stdout || typeof stdout !== "string") {
        return 0;
    }
    try {
        const env = extractAgyEnvelope(stdout) || extractJsonObject(stdout);
        if (env && typeof env === "object") {
            if (env.usage && typeof env.usage.total_tokens === "number" && Number.isFinite(env.usage.total_tokens)) {
                return env.usage.total_tokens;
            }
        }
    } catch {
        /* ignore parse error */
    }
    return 0;
}

export function budgetExceededResult(role, message, details = {}) {
    return {
        ...emptyResult(role, {
            status: "ERROR",
            summary: message,
            blockers: ["budget-exceeded"],
            recommendedNextStep: "Increase the configured budget ceiling or reduce task scope.",
        }),
        errorCode: "budget-exceeded",
        message,
        details,
    };
}

export function resolveEffectiveCeilings(input = {}) {
    const source = (input.budgetCeilings && typeof input.budgetCeilings === "object")
        ? input.budgetCeilings
        : (input.ceilings && typeof input.ceilings === "object")
            ? input.ceilings
            : {};
    return {
        maxAttempts: typeof input.maxAttempts === "number" && input.maxAttempts > 0
            ? Math.round(input.maxAttempts)
            : typeof source.maxAttempts === "number" && source.maxAttempts > 0
                ? Math.round(source.maxAttempts)
                : BUDGET_CEILINGS.maxAttempts,
        maxWallClockMs: typeof input.maxWallClockMs === "number" && input.maxWallClockMs > 0
            ? Math.round(input.maxWallClockMs)
            : typeof source.maxWallClockMs === "number" && source.maxWallClockMs > 0
                ? Math.round(source.maxWallClockMs)
                : BUDGET_CEILINGS.maxWallClockMs,
        maxTotalTokens: typeof input.maxTotalTokens === "number" && input.maxTotalTokens > 0
            ? Math.round(input.maxTotalTokens)
            : typeof source.maxTotalTokens === "number" && source.maxTotalTokens > 0
                ? Math.round(source.maxTotalTokens)
                : BUDGET_CEILINGS.maxTotalTokens,
        maxRunsPerSession: typeof input.maxRunsPerSession === "number" && input.maxRunsPerSession > 0
            ? Math.round(input.maxRunsPerSession)
            : typeof source.maxRunsPerSession === "number" && source.maxRunsPerSession > 0
                ? Math.round(source.maxRunsPerSession)
                : BUDGET_CEILINGS.maxRunsPerSession,
    };
}

async function executeWorkerAttempts(input, spec, effectiveWorkspace, worktreePath, timeoutMs, policy, ceilings, retryStartTime) {
    let lastError = null;
    let accumulatedTotalTokens = 0;
    const workerInput = worktreePath
        ? { ...input, workspace: worktreePath, logicalWorkspace: input.workspace }
        : input;
    const args = buildAgyArgs(workerInput, spec);
    const maxAttempts = ceilings.maxAttempts ?? policy.maxAttempts ?? 3;
    const hardCapTotalRetryMs = policy.hardCapTotalRetryMs ?? 600_000;

    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        const workspaceBefore = spec.kind === "write"
            ? getWorkspaceFingerprint(effectiveWorkspace)
            : null;

        let spawned = null;
        let runError = null;

        try {
            spawned = await spawnAgy(args, { cwd: effectiveWorkspace, timeoutMs });
        }
        catch (err) {
            runError = err instanceof BridgeError
                ? err
                : new BridgeError("INTERNAL_BRIDGE_ERROR", err instanceof Error ? err.message : String(err));
        }

        if (spawned) {
            writeLog(input.role, {
                attempt,
                argv: spawned.argv.map((a, i) => (i === 1 ? "[prompt]" : a)),
                exitCode: spawned.exitCode,
                timedOut: spawned.timedOut,
                stderrTail: spawned.stderr.slice(-4000),
                stdoutTail: spawned.stdout.slice(-4000),
            });

            const attemptTokens = extractTokensFromStdout(spawned.stdout);
            accumulatedTotalTokens += attemptTokens;

            if (spawned.timedOut || spawned.exitCode !== 0) {
                const code = classifyAgyFailure({
                    exitCode: spawned.exitCode,
                    stdout: spawned.stdout,
                    stderr: spawned.stderr,
                    timedOut: spawned.timedOut,
                    signal: spawned.signal,
                    agyMissing: agyMissing(),
                });
                runError = new BridgeError(code, `agy exited ${spawned.exitCode}`, spawned.stderr.slice(-2000));
            }
            else {
                try {
                    const parsed = parseWorkerResult(input.role, spawned.stdout);
                    if (worktreePath) {
                        parsed.worktreePath = worktreePath;
                        if (Array.isArray(parsed.evidence)) {
                            parsed.evidence.push({
                                path: worktreePath,
                                finding: `Git worktree isolation path: ${worktreePath}`,
                            });
                        }
                    }
                    if (input.sessionKey) {
                        try {
                            recordSessionFromEnvelope({
                                sessionKey: input.sessionKey,
                                envelope: spawned.stdout,
                                role: input.role,
                                workspace: input.logicalWorkspace ?? input.workspace,
                                lastStatus: parsed.status,
                            }, input.sessionsPath);
                        }
                        catch (persistErr) {
                            console.warn(`[agy-runner] Failed to persist session '${input.sessionKey}': ${persistErr instanceof Error ? persistErr.message : String(persistErr)}`);
                        }
                    }
                    return parsed;
                }
                catch (err) {
                    runError = new BridgeError("INVALID_OUTPUT", err instanceof Error ? err.message : "Invalid worker JSON", spawned.stdout.slice(0, 2000));
                }
            }
        }

        lastError = runError;

        if (!runError) {
            break;
        }

        const isTransient = isTransientFailure({
            err: runError,
            code: runError.code,
            stdout: spawned?.stdout ?? "",
            stderr: spawned?.stderr ?? "",
            exitCode: spawned?.exitCode ?? null,
            timedOut: spawned?.timedOut ?? false,
        });

        if (!isTransient) {
            // Do NOT retry genuinely deterministic failures (e.g. 400 invalid-argument, auth, missing agy)
            throw runError;
        }

        /*
         * CRITICAL SAFETY NUANCE:
         * Retrying a WRITE-role worker is only safe when the previous attempt provably produced no
         * side effects. For kind === 'write' roles (implement, test, debug), we only retry when the
         * failure occurred before any output was received AND the workspace was not modified.
         * Otherwise, we surface the error rather than blindly re-running a worker that may have
         * partially edited files, left syntax errors, or corrupted workspace state.
         */
        if (spec.kind === "write") {
            const outputReceived = Boolean(spawned?.stdout && spawned.stdout.trim().length > 0);
            const workspaceAfter = getWorkspaceFingerprint(effectiveWorkspace);
            const workspaceModified = wasWorkspaceModified(workspaceBefore, workspaceAfter);

            if (outputReceived || workspaceModified) {
                // Side effects occurred or cannot be proven absent: surface error immediately
                throw runError;
            }
        }

        /*
         * BUDGET CEILING LIMITATION:
         * Token usage is only knowable after an attempt completes (from the agy envelope output),
         * so the token ceiling is enforced strictly between attempts at attempt boundaries.
         * A running agy child process is not interrupted for token consumption during an attempt.
         * However, per-attempt wall-clock time is already bounded by the existing per-attempt
         * timeout (timeoutMs / computePrintTimeoutSeconds), and total run wall-clock is checked
         * across attempts.
         */

        // 1. Evaluate accumulated total tokens against maxTotalTokens ceiling
        if (typeof ceilings.maxTotalTokens === "number" && accumulatedTotalTokens >= ceilings.maxTotalTokens) {
            console.warn(`[agy-runner] Budget ceiling tripped for maxTotalTokens: observed ${accumulatedTotalTokens} tokens, configured limit is ${ceilings.maxTotalTokens} tokens. Stopping retries.`);
            return budgetExceededResult(
                input.role,
                `Budget ceiling exceeded for maxTotalTokens: observed ${accumulatedTotalTokens} tokens, configured limit is ${ceilings.maxTotalTokens} tokens`,
                { ceiling: "maxTotalTokens", observed: accumulatedTotalTokens, limit: ceilings.maxTotalTokens }
            );
        }

        // 2. Evaluate accumulated wall-clock elapsed against maxWallClockMs ceiling
        const totalElapsed = Date.now() - retryStartTime;
        if (typeof ceilings.maxWallClockMs === "number" && totalElapsed >= ceilings.maxWallClockMs) {
            console.warn(`[agy-runner] Budget ceiling tripped for maxWallClockMs: observed ${totalElapsed}ms, configured limit is ${ceilings.maxWallClockMs}ms. Stopping retries.`);
            return budgetExceededResult(
                input.role,
                `Budget ceiling exceeded for maxWallClockMs: observed ${totalElapsed}ms, configured limit is ${ceilings.maxWallClockMs}ms`,
                { ceiling: "maxWallClockMs", observed: totalElapsed, limit: ceilings.maxWallClockMs }
            );
        }

        // 3. Evaluate maxAttempts ceiling
        if (attempt >= maxAttempts) {
            console.warn(`[agy-runner] Maximum attempts (${maxAttempts}) reached for role '${input.role}'. Stopping retries.`);
            return budgetExceededResult(
                input.role,
                `Budget ceiling exceeded for maxAttempts: observed ${attempt} attempts, configured limit is ${maxAttempts}`,
                { ceiling: "maxAttempts", observed: attempt, limit: maxAttempts }
            );
        }

        const delayMs = calculateBackoffMs(attempt, policy);
        if (totalElapsed + delayMs > hardCapTotalRetryMs) {
            // Exceeded total retry time cap: surface error
            break;
        }
        if (typeof ceilings.maxWallClockMs === "number" && totalElapsed + delayMs >= ceilings.maxWallClockMs) {
            console.warn(`[agy-runner] Budget ceiling will be exceeded during backoff for maxWallClockMs (${totalElapsed + delayMs}ms >= ${ceilings.maxWallClockMs}ms). Stopping retries.`);
            return budgetExceededResult(
                input.role,
                `Budget ceiling exceeded for maxWallClockMs: observed ${totalElapsed}ms (with backoff: ${totalElapsed + delayMs}ms), configured limit is ${ceilings.maxWallClockMs}ms`,
                { ceiling: "maxWallClockMs", observed: totalElapsed + delayMs, limit: ceilings.maxWallClockMs }
            );
        }

        console.warn(`[agy-runner] Transient failure (${runError.code}) on attempt ${attempt}/${maxAttempts} for role '${input.role}'. Retrying in ${delayMs}ms...`);
        await sleep(delayMs);
    }

    throw lastError ?? new BridgeError("INTERNAL_BRIDGE_ERROR", "Worker run failed without error");
}

export async function runWorker(input) {
    const injected = faultInject();
    if (injected) {
        throw new BridgeError(injected, `Fault injection: ${injected}`, "AGY_FAULT_INJECT");
    }
    const spec = ROLE_SPECS[input.role];
    const timeoutMs = input.timeoutMs ?? spec.timeoutMs;
    const policy = input.retryPolicy ?? input.policy ?? RETRY_POLICY ?? DEFAULT_RETRY_POLICY;
    const ceilings = resolveEffectiveCeilings(input);
    const retryStartTime = Date.now();

    // Enforce maxRunsPerSession ceiling before starting worker run
    if (input.sessionKey && typeof ceilings.maxRunsPerSession === "number") {
        const session = getSession(input.sessionKey, input.sessionsPath);
        if (session && typeof session.turns === "number" && session.turns >= ceilings.maxRunsPerSession) {
            console.warn(`[agy-runner] Budget ceiling tripped for maxRunsPerSession: observed ${session.turns} runs, configured limit is ${ceilings.maxRunsPerSession}.`);
            return budgetExceededResult(
                input.role,
                `Budget ceiling exceeded for maxRunsPerSession: observed ${session.turns} runs, configured limit is ${ceilings.maxRunsPerSession}`,
                { ceiling: "maxRunsPerSession", observed: session.turns, limit: ceilings.maxRunsPerSession }
            );
        }
    }

    const isWriteRole = spec.kind === "write";
    const permitsAutonomy = Boolean(spec.highAutonomyOptIn) && (input.highAutonomy !== false);
    const useWorktree = isWriteRole && permitsAutonomy;

    if (!useWorktree) {
        return await executeWorkerAttempts(
            input,
            spec,
            input.workspace,
            null,
            timeoutMs,
            policy,
            ceilings,
            retryStartTime
        );
    }

    const gate = input.gate ?? defaultGate;
    const worktreeSlot = await gate.acquire("worktree");
    let worktreePath = null;

    try {
        try {
            cleanupStaleWorktrees({ workspace: input.workspace });
        } catch {
            /* ignore stale cleanup error */
        }

        const runId = input.runId ?? `${input.role}-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
        const wtResult = createWorktree(input.workspace, runId);
        if (wtResult === "not-a-git-repo") {
            console.warn(`[agy-runner] Workspace '${input.workspace}' is not inside a git repository; falling back to in-place execution.`);
        } else {
            worktreePath = wtResult;
            try {
                fs.writeFileSync(path.join(worktreePath, ".agy-disposable"), "disposable worktree\n", "utf8");
            } catch {
                /* ignore marker write error */
            }
        }

        const effectiveWorkspace = worktreePath ?? input.workspace;
        const writeResult = await executeWorkerAttempts(input, spec, effectiveWorkspace, worktreePath, timeoutMs, policy, ceilings, retryStartTime);
        return await applyReviewGate(writeResult, input, spec, worktreePath, runWorker);
    } finally {
        worktreeSlot.release();
    }
}

export function errorResult(role, err) {
    if (err instanceof BridgeError) {
        return emptyResult(role, {
            status: "ERROR",
            summary: `${err.code}: ${err.message}`,
            blockers: [err.code],
            risks: err.details ? [err.details] : [],
            recommendedNextStep: err.code === "budget-exceeded"
                ? "Increase the configured budget ceiling or reduce task scope."
                : "Follow COST_ROUTING.md: fall back to MAI-Code-1.1-Flash if this is a quota/rate/service error.",
            errorCode: err.code,
            message: err.message,
        });
    }
    return emptyResult(role, {
        status: "ERROR",
        summary: err instanceof Error ? err.message : String(err),
        blockers: ["INTERNAL_BRIDGE_ERROR"],
        errorCode: "INTERNAL_BRIDGE_ERROR",
        message: err instanceof Error ? err.message : String(err),
    });
}
