#!/usr/bin/env node
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
    DEFAULT_BUDGET_CEILINGS,
    BUDGET_CEILINGS,
    budgetCeilings,
    getBudgetCeilings,
    resolveBudgetCeilings,
} from "./config.js";
import {
    runWorker,
    extractTokensFromStdout,
    budgetExceededResult,
    resolveEffectiveCeilings,
} from "./agy-runner.js";
import { saveSession } from "./sessions.js";

console.log("=== RUNNING AGY BUDGET CEILINGS SELF-TEST ===");

// 1. Assert configuration knobs and defaults in config.js
console.log("\n--- Checking dist/config.js exports and defaults ---");
assert.ok(DEFAULT_BUDGET_CEILINGS, "DEFAULT_BUDGET_CEILINGS must be defined");
assert.strictEqual(typeof DEFAULT_BUDGET_CEILINGS.maxAttempts, "number");
assert.strictEqual(DEFAULT_BUDGET_CEILINGS.maxAttempts, 3);
assert.strictEqual(typeof DEFAULT_BUDGET_CEILINGS.maxWallClockMs, "number");
assert.strictEqual(DEFAULT_BUDGET_CEILINGS.maxWallClockMs, 1_800_000);
assert.strictEqual(typeof DEFAULT_BUDGET_CEILINGS.maxTotalTokens, "number");
assert.strictEqual(DEFAULT_BUDGET_CEILINGS.maxTotalTokens, 5_000_000);  // raised from 1M: measured live runs cost 92k-305k tokens per trivial task
assert.strictEqual(typeof DEFAULT_BUDGET_CEILINGS.maxRunsPerSession, "number");
assert.strictEqual(DEFAULT_BUDGET_CEILINGS.maxRunsPerSession, 50);

assert.strictEqual(BUDGET_CEILINGS, budgetCeilings, "budgetCeilings alias must match BUDGET_CEILINGS");
assert.strictEqual(getBudgetCeilings(), BUDGET_CEILINGS, "getBudgetCeilings() must return BUDGET_CEILINGS");

// Test resolveBudgetCeilings with overrides
const customCeilings = resolveBudgetCeilings({
    budgetCeilings: {
        maxAttempts: 5,
        maxWallClockMs: 600_000,
        maxTotalTokens: 500_000,
        maxRunsPerSession: 20,
    },
});
assert.strictEqual(customCeilings.maxAttempts, 5);
assert.strictEqual(customCeilings.maxWallClockMs, 600_000);
assert.strictEqual(customCeilings.maxTotalTokens, 500_000);
assert.strictEqual(customCeilings.maxRunsPerSession, 20);
console.log("PASS: dist/config.js exports, knobs, defaults, and overrides verified.");

// 2. Set up mock agy environment (NO real agy binary invoked)
const testDir = path.join(os.tmpdir(), `agy-budget-test-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`);
fs.mkdirSync(testDir, { recursive: true });
const mockAgyPath = path.join(testDir, "mock-agy.mjs");
const counterFile = path.join(testDir, "counter.txt");
const modeFile = path.join(testDir, "mode.json");

// Write mock agy executable script
const mockAgyCode = `
import fs from "node:fs";

const modeFilePath = ${JSON.stringify(modeFile)};
const counterFilePath = ${JSON.stringify(counterFile)};

// Increment invocation counter
let count = 0;
try {
    if (fs.existsSync(counterFilePath)) {
        count = parseInt(fs.readFileSync(counterFilePath, "utf8"), 10) || 0;
    }
} catch {}
count++;
fs.writeFileSync(counterFilePath, String(count), "utf8");

// Read current test mode configuration
let modeConfig = { mode: "success" };
try {
    if (fs.existsSync(modeFilePath)) {
        modeConfig = JSON.parse(fs.readFileSync(modeFilePath, "utf8"));
    }
} catch {}

if (modeConfig.delayMs) {
    const start = Date.now();
    while (Date.now() - start < modeConfig.delayMs) {
        // busy wait sleep
    }
}

if (modeConfig.mode === "success") {
    const envelope = {
        conversation_id: "mock-conv-1",
        status: "SUCCESS",
        response: {
            status: "SUCCESS",
            role: "explore",
            summary: "Mock single attempt completed successfully",
            evidence: [{ path: "test.js", finding: "test passed" }],
            filesRead: ["test.js"],
            filesChanged: [],
            commandsRun: [],
            tests: [],
            risks: [],
            blockers: [],
            recommendedNextStep: "Done",
            confidence: "high",
        },
        duration_seconds: 0.1,
        num_turns: 1,
        usage: {
            input_tokens: modeConfig.tokens ?? 20000,
            output_tokens: 500,
            thinking_tokens: 0,
            cache_read_tokens: 0,
            total_tokens: (modeConfig.tokens ?? 20000) + 500,
        },
    };
    process.stdout.write(JSON.stringify(envelope));
    process.exit(0);
} else if (modeConfig.mode === "transient-fail") {
    const tokens = modeConfig.tokens ?? 25000;
    const envelope = {
        conversation_id: "mock-conv-fail",
        status: "ERROR",
        usage: {
            input_tokens: tokens - 100,
            output_tokens: 100,
            total_tokens: tokens,
        },
    };
    process.stdout.write(JSON.stringify(envelope));
    process.stderr.write("HTTP 503 Service Unavailable: upstream overloaded\\n");
    process.exit(1);
} else {
    process.stderr.write("Unknown mode\\n");
    process.exit(1);
}
`;

fs.writeFileSync(mockAgyPath, mockAgyCode, "utf8");

// Set environment to direct all spawnAgy calls to mock script
process.env.AGY_BIN = mockAgyPath;

function setMockMode(cfg) {
    fs.writeFileSync(modeFile, JSON.stringify(cfg), "utf8");
}

function getInvocationCount() {
    try {
        if (fs.existsSync(counterFile)) {
            return parseInt(fs.readFileSync(counterFile, "utf8"), 10) || 0;
        }
    } catch {}
    return 0;
}

function resetInvocationCount() {
    try {
        fs.writeFileSync(counterFile, "0", "utf8");
    } catch {}
}

const fastRetryPolicy = {
    maxAttempts: 3,
    baseBackoffMs: 1,
    backoffFactor: 1,
    maxJitterMs: 0,
    hardCapTotalRetryMs: 10_000,
};

async function main() {
    try {
        // =========================================================================
        // ASSERTION 1: Normal single-attempt run permitted when no ceilings configured
        // (Default behaviour preserved)
        // =========================================================================
        console.log("\n--- Assertion 1: Default behaviour preserved (no ceilings configured) ---");
        setMockMode({ mode: "success", tokens: 23000 });
        resetInvocationCount();

        const normalResult = await runWorker({
            role: "explore",
            goal: "Normal run with default ceilings",
            workspace: testDir,
            highAutonomy: false,
            policy: fastRetryPolicy,
        });

        assert.strictEqual(normalResult.status, "SUCCESS", "Normal run status must be SUCCESS");
        assert.strictEqual(normalResult.role, "explore");
        assert.strictEqual(normalResult.errorCode, undefined, "Normal run must not have errorCode");
        assert.strictEqual(getInvocationCount(), 1, "Normal run must execute exactly 1 attempt");
        console.log("PASS: Normal single-attempt run permitted with default settings (default behaviour preserved).");

        // =========================================================================
        // ASSERTION 2: Token ceiling trips when accumulated usage exceeds maximum
        // Yields errorCode 'budget-exceeded'
        // =========================================================================
        console.log("\n--- Assertion 2: Token ceiling trips and yields errorCode 'budget-exceeded' ---");
        // Attempt 1 will fail transiently and consume 30,000 tokens
        setMockMode({ mode: "transient-fail", tokens: 30000 });
        resetInvocationCount();

        const tokenCeilingResult = await runWorker({
            role: "explore",
            goal: "Token ceiling test",
            workspace: testDir,
            highAutonomy: false,
            policy: fastRetryPolicy,
            maxTotalTokens: 20000, // Configured limit is 20,000 tokens; attempt consumed 30,000
        });

        assert.strictEqual(tokenCeilingResult.status, "ERROR", "Status must be ERROR on budget exceeded");
        assert.strictEqual(tokenCeilingResult.errorCode, "budget-exceeded", "errorCode must be exactly 'budget-exceeded'");
        assert.ok(tokenCeilingResult.blockers.includes("budget-exceeded"), "blockers must include 'budget-exceeded'");
        assert.ok(tokenCeilingResult.message.includes("maxTotalTokens"), "message must name 'maxTotalTokens'");
        assert.ok(tokenCeilingResult.message.includes("30000"), "message must report observed token count 30000");
        assert.ok(tokenCeilingResult.message.includes("20000"), "message must report configured limit 20000");
        assert.strictEqual(getInvocationCount(), 1, "Must stop retrying after attempt 1 when token ceiling is tripped");
        console.log("PASS: Token ceiling tripped at attempt boundary, yielded errorCode 'budget-exceeded', stopped retrying.");

        // =========================================================================
        // ASSERTION 3: Wall-clock ceiling trips when elapsed time exceeds maximum
        // =========================================================================
        console.log("\n--- Assertion 3: Wall-clock ceiling trips when elapsed time exceeds maximum ---");
        // Attempt 1 will fail transiently after a 60ms delay
        setMockMode({ mode: "transient-fail", tokens: 1000, delayMs: 60 });
        resetInvocationCount();

        const wallClockResult = await runWorker({
            role: "explore",
            goal: "Wall-clock ceiling test",
            workspace: testDir,
            highAutonomy: false,
            policy: fastRetryPolicy,
            maxWallClockMs: 40, // Configured limit 40ms; attempt elapsed is >= 60ms
        });

        assert.strictEqual(wallClockResult.status, "ERROR", "Status must be ERROR on wall-clock budget exceeded");
        assert.strictEqual(wallClockResult.errorCode, "budget-exceeded", "errorCode must be exactly 'budget-exceeded'");
        assert.ok(wallClockResult.message.includes("maxWallClockMs"), "message must name 'maxWallClockMs'");
        assert.ok(wallClockResult.message.includes("40"), "message must name configured limit 40ms");
        assert.strictEqual(getInvocationCount(), 1, "Must stop retrying when wall-clock ceiling is tripped");
        console.log("PASS: Wall-clock ceiling tripped when elapsed time exceeded maximum.");

        // =========================================================================
        // ASSERTION 4: maxAttempts is respected
        // =========================================================================
        console.log("\n--- Assertion 4: maxAttempts is respected ---");
        // Mode: transient failures without exceeding tokens (tokens low) or wall-clock (fast)
        setMockMode({ mode: "transient-fail", tokens: 100 });
        resetInvocationCount();

        const maxAttemptsResult = await runWorker({
            role: "explore",
            goal: "maxAttempts test",
            workspace: testDir,
            highAutonomy: false,
            policy: fastRetryPolicy,
            maxAttempts: 2, // Configured limit is 2 attempts
            maxTotalTokens: 1_000_000,
            maxWallClockMs: 600_000,
        });

        const attemptsExecuted = getInvocationCount();
        assert.strictEqual(attemptsExecuted, 2, `Expected exactly 2 attempts executed; saw ${attemptsExecuted}`);
        assert.strictEqual(maxAttemptsResult.status, "ERROR");
        assert.strictEqual(maxAttemptsResult.errorCode, "budget-exceeded");
        assert.ok(maxAttemptsResult.message.includes("maxAttempts"), "message must name 'maxAttempts'");
        assert.ok(maxAttemptsResult.message.includes("2"), "message must report limit 2");
        console.log("PASS: maxAttempts was strictly respected (stopped retrying after 2 attempts).");

        // =========================================================================
        // ASSERTION 5: Failure message actually names the tripped ceiling, observed, and limit
        // =========================================================================
        console.log("\n--- Assertion 5: Failure message names tripped ceiling, observed, and limit ---");

        // Test token failure message structure
        assert.ok(/maxTotalTokens/.test(tokenCeilingResult.message), "Token message must contain ceiling name 'maxTotalTokens'");
        assert.ok(/observed \d+ tokens/.test(tokenCeilingResult.message), "Token message must contain observed value");
        assert.ok(/configured limit is \d+/.test(tokenCeilingResult.message), "Token message must contain configured limit");

        // Test wall-clock failure message structure
        assert.ok(/maxWallClockMs/.test(wallClockResult.message), "Wall-clock message must contain ceiling name 'maxWallClockMs'");
        assert.ok(/observed \d+ms/.test(wallClockResult.message), "Wall-clock message must contain observed value in ms");
        assert.ok(/configured limit is 40ms/.test(wallClockResult.message), "Wall-clock message must contain configured limit");

        // Test maxAttempts failure message structure
        assert.ok(/maxAttempts/.test(maxAttemptsResult.message), "maxAttempts message must contain ceiling name 'maxAttempts'");
        assert.ok(/observed 2 attempts/.test(maxAttemptsResult.message), "maxAttempts message must contain observed attempts");
        assert.ok(/configured limit is 2/.test(maxAttemptsResult.message), "maxAttempts message must contain configured limit");

        console.log("Token ceiling message:      ", tokenCeilingResult.message);
        console.log("Wall-clock ceiling message: ", wallClockResult.message);
        console.log("maxAttempts ceiling message:", maxAttemptsResult.message);
        console.log("PASS: Failure messages verified to explicitly name ceiling, observed value, and limit.");

        // =========================================================================
        // ASSERTION 6: maxRunsPerSession ceiling is enforced
        // =========================================================================
        console.log("\n--- Assertion 6: maxRunsPerSession ceiling is enforced ---");
        const sessionStorePath = path.join(testDir, "test-sessions.json");
        saveSession({
            sessionKey: "session-budget-test",
            conversationId: "conv-123",
            role: "explore",
            workspace: testDir,
            turns: 10,
        }, sessionStorePath);

        const sessionCeilingResult = await runWorker({
            role: "explore",
            goal: "maxRunsPerSession test",
            workspace: testDir,
            sessionKey: "session-budget-test",
            sessionsPath: sessionStorePath,
            maxRunsPerSession: 10, // Cap is 10; session already has 10 runs
        });

        assert.strictEqual(sessionCeilingResult.status, "ERROR");
        assert.strictEqual(sessionCeilingResult.errorCode, "budget-exceeded");
        assert.ok(sessionCeilingResult.message.includes("maxRunsPerSession"));
        assert.ok(sessionCeilingResult.message.includes("10"));
        console.log("maxRunsPerSession message:  ", sessionCeilingResult.message);
        console.log("PASS: maxRunsPerSession ceiling verified.");

        console.log("\n=== ALL BUDGET CEILING SELF-TESTS PASSED SUCCESSFULLY ===");
    } finally {
        try {
            fs.rmSync(testDir, { recursive: true, force: true });
        } catch {}
    }
}

main().catch((err) => {
    console.error("FAIL:", err);
    process.exit(1);
});
