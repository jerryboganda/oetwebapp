import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { BridgeError, classifyAgyFailure } from "./errors.js";
import { emptyResult, parseWorkerResult } from "./schemas.js";
import { PINNED_EFFORT, PINNED_MODEL, ensureUseG1CreditsFalse, highAutonomyAllowed, logsDir, } from "./security.js";
import { ROLE_SPECS, rolePrompt } from "./roles.js";
import { computePrintTimeoutSeconds } from "./config.js";
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
export async function runWorker(input) {
    const injected = faultInject();
    if (injected) {
        throw new BridgeError(injected, `Fault injection: ${injected}`, "AGY_FAULT_INJECT");
    }
    const spec = ROLE_SPECS[input.role];
    const args = buildAgyArgs(input, spec);
    const timeoutMs = input.timeoutMs ?? spec.timeoutMs;
    let spawned;
    try {
        spawned = await spawnAgy(args, { cwd: input.workspace, timeoutMs });
    }
    catch (err) {
        if (err instanceof BridgeError)
            throw err;
        throw new BridgeError("INTERNAL_BRIDGE_ERROR", err instanceof Error ? err.message : String(err));
    }
    writeLog(input.role, {
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
        throw new BridgeError(code, `agy exited ${spawned.exitCode}`, spawned.stderr.slice(-2000));
    }
    try {
        return parseWorkerResult(input.role, spawned.stdout);
    }
    catch (err) {
        throw new BridgeError("INVALID_OUTPUT", err instanceof Error ? err.message : "Invalid worker JSON", spawned.stdout.slice(0, 2000));
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
