import { execFileSync, spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { BridgeError, classifyAgyFailure } from "./errors.js";
import { emptyResult, parseWorkerResult } from "./schemas.js";
import { PINNED_EFFORT, PINNED_MODEL, ensureUseG1CreditsFalse, highAutonomyAllowed, logsDir, } from "./security.js";
import { ROLE_SPECS, rolePrompt } from "./roles.js";
import { computePrintTimeoutSeconds, DEFAULT_RETRY_POLICY, RETRY_POLICY } from "./config.js";
import { defaultGate } from "./concurrency.js";
import { createWorktree, cleanupStaleWorktrees } from "./worktree.js";

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

export function buildAgyArgs(input, spec) {
    const prompt = rolePrompt({
        role: input.role,
        goal: input.goal,
        context: input.context,
        workspace: input.workspace,
        extraConstraints: input.extraConstraints,
    });
    const timeout = input.timeoutMs ?? spec.timeoutMs;
    const schemaFile = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../schemas/worker-result.schema.json");
    const args = [
        "-p",
        prompt,
        "--model",
        PINNED_MODEL,
        "--effort",
        PINNED_EFFORT,
        "--output-format",
        "json",
        "--json-schema",
        schemaFile,
        "--print-timeout",
        `${computePrintTimeoutSeconds(timeout)}s`,
        "--mode",
        spec.mode,
    ];
    if (spec.sandbox)
        args.push("--sandbox");
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

async function executeWorkerAttempts(input, spec, effectiveWorkspace, worktreePath, timeoutMs, policy, maxAttempts, hardCapTotalRetryMs, retryStartTime) {
    let lastError = null;
    const workerInput = worktreePath ? { ...input, workspace: worktreePath } : input;
    const args = buildAgyArgs(workerInput, spec);

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

        if (attempt >= maxAttempts) {
            break;
        }

        const delayMs = calculateBackoffMs(attempt, policy);
        const totalElapsed = Date.now() - retryStartTime;
        if (totalElapsed + delayMs > hardCapTotalRetryMs) {
            // Exceeded total retry time cap: surface error
            break;
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
    const policy = RETRY_POLICY || DEFAULT_RETRY_POLICY;
    const maxAttempts = policy.maxAttempts ?? 3;
    const hardCapTotalRetryMs = policy.hardCapTotalRetryMs ?? 600_000;
    const retryStartTime = Date.now();

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
            maxAttempts,
            hardCapTotalRetryMs,
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
        return await executeWorkerAttempts(
            input,
            spec,
            effectiveWorkspace,
            worktreePath,
            timeoutMs,
            policy,
            maxAttempts,
            hardCapTotalRetryMs,
            retryStartTime
        );
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
            recommendedNextStep: "Follow COST_ROUTING.md: fall back to MAI-Code-1.1-Flash if this is a quota/rate/service error.",
        });
    }
    return emptyResult(role, {
        status: "ERROR",
        summary: err instanceof Error ? err.message : String(err),
        blockers: ["INTERNAL_BRIDGE_ERROR"],
    });
}
