import { McpServer } from "@modelcontextprotocol/server";
import * as z from "zod/v4";
import { BridgeError, shouldFallbackToMai } from "./errors.js";
import { defaultGate } from "./concurrency.js";
import { errorResult, runWorker, spawnAgy } from "./agy-runner.js";
import { PINNED_EFFORT, PINNED_MODEL, assertAllowlistedCommand, ensureUseG1CreditsFalse, resolveWorkspace } from "./security.js";
import { ROLE_SPECS } from "./roles.js";
const taskShape = {
    goal: z.string().min(1),
    context: z.string().optional(),
    workspace: z.string().optional(),
    extraConstraints: z.string().optional(),
    highAutonomy: z.boolean().optional(),
};
function text(obj) {
    return {
        content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
    };
}
async function withGate(kind, fn) {
    const slot = await defaultGate.acquire(kind);
    try {
        return await fn();
    }
    finally {
        slot.release();
    }
}
async function runRole(role, args) {
    try {
        const workspace = resolveWorkspace(args.workspace);
        const spec = ROLE_SPECS[role];
        const result = await withGate(spec.kind, () => runWorker({
            role,
            goal: args.goal,
            context: args.context,
            workspace,
            extraConstraints: args.extraConstraints,
            highAutonomy: args.highAutonomy,
        }));
        return text(result);
    }
    catch (err) {
        const result = errorResult(role, err);
        const code = err instanceof BridgeError ? err.code : "INTERNAL_BRIDGE_ERROR";
        return text({
            ...result,
            fallbackToMai: shouldFallbackToMai(code),
            errorCode: code,
        });
    }
}
export function createAntigravityServer() {
    const server = new McpServer({
        name: "antigravity-workers",
        version: "1.0.0",
    });
    server.registerTool("ag_health", {
        description: "Check agy install, pinned model gemini-3.8-flash-high, and account-quota settings (useG1Credits false).",
        inputSchema: z.object({
            workspace: z.string().optional(),
        }),
    }, async ({ workspace }) => {
        const root = resolveWorkspace(workspace);
        const settings = ensureUseG1CreditsFalse();
        let modelsOut = "";
        let modelsErr = "";
        let modelsExit = null;
        let modelPresent = false;
        try {
            const spawned = await spawnAgy(["models"], { cwd: root, timeoutMs: 60_000 });
            modelsOut = spawned.stdout;
            modelsErr = spawned.stderr;
            modelsExit = spawned.exitCode;
            modelPresent = `${spawned.stdout}\n${spawned.stderr}`.includes(PINNED_MODEL);
        }
        catch (err) {
            modelsErr = err instanceof Error ? err.message : String(err);
        }
        return text({
            status: modelPresent && settings.useG1Credits === false ? "SUCCESS" : "PARTIAL",
            role: "health",
            summary: modelPresent
                ? `agy reachable; ${PINNED_MODEL} listed; useG1Credits=${settings.useG1Credits}`
                : "agy health incomplete",
            pinnedModel: PINNED_MODEL,
            pinnedEffort: PINNED_EFFORT,
            useG1Credits: settings.useG1Credits,
            settingsPath: settings.path,
            modelPresent,
            modelsExit,
            concurrency: defaultGate.snapshot(),
            evidence: [
                { path: settings.path, finding: `useG1Credits=${settings.useG1Credits}` },
            ],
            filesRead: [settings.path],
            filesChanged: [],
            commandsRun: [{ command: "agy models", exitCode: modelsExit ?? -1, result: modelPresent ? "listed" : modelsErr.slice(0, 500) }],
            tests: [],
            risks: modelPresent ? [] : ["Pinned model not confirmed in agy models output"],
            blockers: [],
            recommendedNextStep: modelPresent
                ? "Call ag_explore on a disposable workspace."
                : "Run official `agy` login if AUTH_REQUIRED, then retry ag_health.",
            confidence: modelPresent ? "high" : "low",
            modelsTail: `${modelsOut}\n${modelsErr}`.slice(-1500),
        });
    });
    server.registerTool("ag_explore", {
        description: "Read-only Antigravity explore worker (gemini-3.8-flash-high, account quota).",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("explore", args));
    server.registerTool("ag_research", {
        description: "Read-only Antigravity research worker (gemini-3.8-flash-high, account quota).",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("research", args));
    server.registerTool("ag_implement", {
        description: "Write Antigravity implement worker. High autonomy only in disposable worktrees.",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("implement", args));
    server.registerTool("ag_test", {
        description: "Antigravity test worker. Runs the smallest relevant check.",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("test", args));
    server.registerTool("ag_debug", {
        description: "Antigravity debug worker. Root-cause then smallest safe fix.",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("debug", args));
    server.registerTool("ag_review", {
        description: "Read-only Antigravity review worker.",
        inputSchema: z.object(taskShape),
    }, async (args) => runRole("review", args));
    server.registerTool("ag_run", {
        description: "Run one allowlisted command in the workspace (git/pnpm/node/vitest/docker/dotnet/python).",
        inputSchema: z.object({
            command: z.string().min(1),
            workspace: z.string().optional(),
        }),
    }, async ({ command, workspace }) => {
        assertAllowlistedCommand(command);
        const root = resolveWorkspace(workspace);
        const { spawn } = await import("node:child_process");
        const result = await new Promise((resolve, reject) => {
            const child = spawn(command, {
                cwd: root,
                shell: true,
                windowsHide: true,
                stdio: ["ignore", "pipe", "pipe"],
            });
            let stdout = "";
            let stderr = "";
            const timer = setTimeout(() => child.kill("SIGTERM"), 60_000);
            child.stdout?.on("data", (c) => {
                stdout += c.toString("utf8");
            });
            child.stderr?.on("data", (c) => {
                stderr += c.toString("utf8");
            });
            child.on("error", reject);
            child.on("close", (exitCode) => {
                clearTimeout(timer);
                resolve({ exitCode, stdout, stderr });
            });
        });
        return text({
            status: result.exitCode === 0 ? "SUCCESS" : "ERROR",
            command,
            exitCode: result.exitCode,
            stdout: result.stdout.slice(-4000),
            stderr: result.stderr.slice(-4000),
        });
    });
    return server;
}
