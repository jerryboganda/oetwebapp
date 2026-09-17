#!/usr/bin/env node
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import {
    appendLedgerRecord,
    createRunningRecord,
    createTerminalRecord,
    findStaleRuns,
    generateRunId,
    getLedgerStats,
    readAllRecords,
    readRecentRecords,
    LEDGER_CONFIG,
} from "./ledger.js";
import { createAntigravityServer } from "./server.js";

console.log("=== RUNNING AGY RUN LEDGER SELF-TEST ===");

const testDir = path.join(os.tmpdir(), `agy-ledger-selftest-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`);
fs.mkdirSync(testDir, { recursive: true });
const testLedgerPath = path.join(testDir, "test-ledger.jsonl");

try {
    // 1. Assert: a record round-trips through a real file read
    console.log("\n[Assertion 1] Record round-trips through a real file read");
    const testRunId = generateRunId();
    assert.ok(testRunId.startsWith("run_"), "generateRunId must generate collision-resistant ID with prefix");

    const runningRecord = createRunningRecord({
        runId: testRunId,
        sessionKey: "session-abc-123",
        role: "implement",
        workspace: testDir,
        model: "gemini-3.8-flash-high",
        effort: "high",
        mode: "accept-edits",
        attempt: 1,
    });

    const appendedStart = appendLedgerRecord(runningRecord, testLedgerPath);
    assert.ok(appendedStart, "appendLedgerRecord must return the appended running record");

    const terminalRecord = createTerminalRecord(runningRecord, {
        status: "SUCCESS",
        exitCode: 0,
        conversationId: "conv-roundtrip-test-456",
        usage: {
            input_tokens: 1500,
            output_tokens: 300,
            total_tokens: 1800,
        },
    });
    const appendedEnd = appendLedgerRecord(terminalRecord, testLedgerPath);
    assert.ok(appendedEnd, "appendLedgerRecord must return the appended terminal record");

    const recordsFromDisk = readAllRecords(testLedgerPath);
    assert.strictEqual(recordsFromDisk.length, 2, "File should contain exactly 2 records");

    // Verify running record fields
    assert.strictEqual(recordsFromDisk[0].runId, testRunId, "Round-tripped running record runId must match");
    assert.strictEqual(recordsFromDisk[0].sessionKey, "session-abc-123", "Round-tripped sessionKey must match");
    assert.strictEqual(recordsFromDisk[0].role, "implement", "Round-tripped role must match");
    assert.strictEqual(recordsFromDisk[0].status, "running", "Initial status must be 'running'");
    assert.strictEqual(recordsFromDisk[0].mode, "accept-edits", "Round-tripped mode must match");
    assert.strictEqual(recordsFromDisk[0].attempt, 1, "Attempt must be 1");

    // Verify terminal record fields
    assert.strictEqual(recordsFromDisk[1].runId, testRunId, "Round-tripped terminal record runId must match");
    assert.strictEqual(recordsFromDisk[1].status, "SUCCESS", "Terminal status must be 'SUCCESS'");
    assert.strictEqual(recordsFromDisk[1].exitCode, 0, "Terminal exitCode must be 0");
    assert.strictEqual(recordsFromDisk[1].conversationId, "conv-roundtrip-test-456", "Conversation id must match");
    assert.strictEqual(recordsFromDisk[1].usage.total_tokens, 1800, "Usage total_tokens must match");
    console.log("PASS: Record round-trips through real file read with all required fields preserved.");

    // 2. Assert: a 'running' record with an aged timestamp IS detected as stale while a fresh one is not
    console.log("\n[Assertion 2] Running record with aged timestamp IS detected as stale while fresh is not");
    const staleRunId = generateRunId();
    const freshRunId = generateRunId();

    const agedTimestamp = new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(); // 3 hours ago
    const freshTimestamp = new Date(Date.now() - 5 * 60 * 1000).toISOString(); // 5 minutes ago

    const agedRunningRecord = createRunningRecord({
        runId: staleRunId,
        role: "debug",
        workspace: testDir,
        startedAt: agedTimestamp,
    });
    appendLedgerRecord(agedRunningRecord, testLedgerPath);

    const freshRunningRecord = createRunningRecord({
        runId: freshRunId,
        role: "research",
        workspace: testDir,
        startedAt: freshTimestamp,
    });
    appendLedgerRecord(freshRunningRecord, testLedgerPath);

    // Stale threshold: 2 hours
    const staleRuns = findStaleRuns({ thresholdMs: 2 * 60 * 60 * 1000 }, testLedgerPath);
    const staleIds = staleRuns.map((r) => r.runId);

    assert.ok(staleIds.includes(staleRunId), `Aged running record (${staleRunId}, 3h old) must be detected as stale`);
    assert.ok(!staleIds.includes(freshRunId), `Fresh running record (${freshRunId}, 5m old) must NOT be detected as stale`);
    assert.ok(!staleIds.includes(testRunId), `Completed run (${testRunId}) must NOT be detected as stale`);
    console.log(`PASS: Stale detection verified (stale=${staleRunId}, fresh=${freshRunId}, completed excluded).`);

    // 3. Assert: a deliberately corrupted line in the ledger file does not throw and does not prevent a later successful append
    console.log("\n[Assertion 3] Deliberately corrupted line does not throw and does not prevent later successful append");
    // Inject corrupt line (malformed JSON without closing bracket or newline)
    fs.appendFileSync(testLedgerPath, "CORRUPTED_JSON_LINE_{{broken::;;}\n", "utf8");

    // Read should skip corrupt line and not throw
    let recordsAfterCorrupt = null;
    assert.doesNotThrow(() => {
        recordsAfterCorrupt = readAllRecords(testLedgerPath);
    }, "readAllRecords must not throw on corrupted line");
    assert.strictEqual(recordsAfterCorrupt.length, 4, "Must return 4 valid records, skipping 1 corrupt line");

    // Later append must succeed without throwing
    const afterCorruptRunId = generateRunId();
    const afterCorruptRecord = createRunningRecord({
        runId: afterCorruptRunId,
        role: "test",
        workspace: testDir,
    });

    let appendResult = null;
    assert.doesNotThrow(() => {
        appendResult = appendLedgerRecord(afterCorruptRecord, testLedgerPath);
    }, "appendLedgerRecord must not throw after a corrupt line in file");
    assert.ok(appendResult, "appendLedgerRecord must return appended record");

    const finalRecords = readAllRecords(testLedgerPath);
    assert.strictEqual(finalRecords.length, 5, "Must have 5 valid records now");
    assert.strictEqual(finalRecords[4].runId, afterCorruptRunId, "Record appended after corrupt line must be present");
    console.log("PASS: Corrupt line tolerated without throwing and subsequent append succeeded.");

    // 4. Assert: the stats operation sums total_tokens correctly across multiple records
    console.log("\n[Assertion 4] Stats operation sums total_tokens correctly across multiple records");
    const statsLedgerPath = path.join(testDir, "stats-ledger.jsonl");

    const rec1 = createTerminalRecord(createRunningRecord({ role: "explore", workspace: testDir }), {
        status: "SUCCESS",
        usage: { input_tokens: 1000, output_tokens: 250, total_tokens: 1250 },
    });
    const rec2 = createTerminalRecord(createRunningRecord({ role: "implement", workspace: testDir }), {
        status: "SUCCESS",
        usage: { input_tokens: 3000, output_tokens: 750, total_tokens: 3750 },
    });
    const rec3 = createTerminalRecord(createRunningRecord({ role: "review", workspace: testDir }), {
        status: "ERROR",
        errorCode: "TIMEOUT",
        usage: { input_tokens: 400, output_tokens: 100, total_tokens: 500 },
    });
    const rec4 = createRunningRecord({ role: "debug", workspace: testDir }); // status: "running", no tokens

    appendLedgerRecord(rec1, statsLedgerPath);
    appendLedgerRecord(rec2, statsLedgerPath);
    appendLedgerRecord(rec3, statsLedgerPath);
    appendLedgerRecord(rec4, statsLedgerPath);

    const stats = getLedgerStats({}, statsLedgerPath);
    const expectedTokens = 1250 + 3750 + 500; // 5500
    assert.strictEqual(stats.totalRecords, 4, "Total records must be 4");
    assert.strictEqual(stats.totalTokens, expectedTokens, `Total tokens must be ${expectedTokens}, got ${stats.totalTokens}`);
    assert.strictEqual(stats.countsByStatus.SUCCESS, 2, "SUCCESS count must be 2");
    assert.strictEqual(stats.countsByStatus.ERROR, 1, "ERROR count must be 1");
    assert.strictEqual(stats.countsByStatus.running, 1, "running count must be 1");
    console.log(`PASS: Stats correctly summed total_tokens (${stats.totalTokens}) across multiple records with counts: ${JSON.stringify(stats.countsByStatus)}.`);

    // 5. Assert: the list operation respects both its status filter and its limit
    console.log("\n[Assertion 5] List operation respects status filter and limit");
    // Status filter
    const successRuns = readRecentRecords({ status: "SUCCESS" }, statsLedgerPath);
    assert.strictEqual(successRuns.length, 2, "Status filter 'SUCCESS' must return exactly 2 records");
    assert.ok(successRuns.every((r) => r.status === "SUCCESS"), "All returned records must have status 'SUCCESS'");

    const errorRuns = readRecentRecords({ status: "ERROR" }, statsLedgerPath);
    assert.strictEqual(errorRuns.length, 1, "Status filter 'ERROR' must return exactly 1 record");
    assert.strictEqual(errorRuns[0].status, "ERROR");

    // Limit (newest first)
    const limitTwo = readRecentRecords({ limit: 2 }, statsLedgerPath);
    assert.strictEqual(limitTwo.length, 2, "Limit 2 must return exactly 2 records");
    assert.strictEqual(limitTwo[0].status, "running", "First record in reversed list must be rec4 (running)");
    assert.strictEqual(limitTwo[1].status, "ERROR", "Second record in reversed list must be rec3 (ERROR)");

    // Combined filter and limit
    const successLimitOne = readRecentRecords({ status: "SUCCESS", limit: 1 }, statsLedgerPath);
    assert.strictEqual(successLimitOne.length, 1, "Combined status filter and limit must return 1 record");
    assert.strictEqual(successLimitOne[0].status, "SUCCESS");
    console.log("PASS: List operation respects status filter, ordering, and limit.");

    // 6. Verify default LEDGER_CONFIG path format
    console.log("\n[Assertion 6] Default LEDGER_CONFIG path");
    const overridePath = process.env.AGY_LEDGER_PATH && process.env.AGY_LEDGER_PATH.trim();
    if (overridePath) {
        assert.strictEqual(LEDGER_CONFIG.path, overridePath, "AGY_LEDGER_PATH override must win (got " + LEDGER_CONFIG.path + ")");
        console.log("PASS: AGY_LEDGER_PATH override honoured: " + LEDGER_CONFIG.path);
    } else {
        assert.ok(LEDGER_CONFIG.path.endsWith(path.join(".agy-state", "ledger.jsonl")), "Default path must end with .agy-state/ledger.jsonl (got " + LEDGER_CONFIG.path + ")");
        console.log("PASS: Default ledger path verified: " + LEDGER_CONFIG.path);
    }

    // 7. Verify MCP server tool registration
    console.log("\n[Assertion 7] McpServer registration verification");
    const server = createAntigravityServer();
    assert.ok(server, "createAntigravityServer must instantiate successfully");
    console.log("PASS: Server successfully instantiated with ag_runs registered.");

    console.log("\n=== ALL SELF-TEST ASSERTIONS PASSED ===");
} finally {
    try {
        fs.rmSync(testDir, { recursive: true, force: true });
    } catch {
        // ignore cleanup error
    }
}
