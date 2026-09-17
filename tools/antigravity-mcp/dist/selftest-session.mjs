#!/usr/bin/env node
import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { ROLE_SPECS } from "./roles.js";
import { buildAgyArgs } from "./agy-runner.js";
import {
    loadSessionStore,
    saveSessionStore,
    recordSessionFromEnvelope,
    getSession,
    listSessions,
    clearSessions,
    pruneSessions,
} from "./sessions.js";

console.log("=== RUNNING AGY SESSION CONTINUITY SELF-TEST ===");

const testDir = path.join(os.tmpdir(), `agy-selftest-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`);
fs.mkdirSync(testDir, { recursive: true });
const testStorePath = path.join(testDir, "sessions.json");
const testWorkspace = path.resolve(path.join(testDir, "workspace"));
fs.mkdirSync(testWorkspace, { recursive: true });

try {
    // 1. Write a FAKE agy JSON envelope containing a conversation id via the real persistence function
    const fakeConvId = "agy-test-conv-987654321";
    const fakeEnvelope = JSON.stringify({
        conversation_id: fakeConvId,
        status: "SUCCESS",
        response: {
            status: "SUCCESS",
            role: "explore",
            summary: "Fake explore result for testing",
            evidence: [{ path: "test.js", finding: "test finding" }],
            filesRead: ["test.js"],
            filesChanged: [],
            commandsRun: [],
            tests: [],
            risks: [],
            blockers: [],
            recommendedNextStep: "Proceed to next turn",
            confidence: "high",
        },
    });

    const testSessionKey = "session-optin-1";
    const savedEntry = recordSessionFromEnvelope({
        sessionKey: testSessionKey,
        envelope: fakeEnvelope,
        role: "explore",
        workspace: testWorkspace,
        lastStatus: "SUCCESS",
    }, testStorePath);

    assert.ok(savedEntry, "recordSessionFromEnvelope must return saved entry");
    assert.strictEqual(savedEntry.conversationId, fakeConvId);
    console.log("PASS: Real persistence function wrote fake envelope to session store.");

    // 2. Assert the id round-trips through a real file read
    const fileContent = fs.readFileSync(testStorePath, "utf8");
    const parsedFromDisk = JSON.parse(fileContent);
    assert.ok(parsedFromDisk.sessions, "Store file on disk must contain sessions object");
    const storedOnDisk = parsedFromDisk.sessions[testSessionKey];
    assert.strictEqual(storedOnDisk.conversationId, fakeConvId, "Round-tripped conversationId must match");
    assert.strictEqual(storedOnDisk.sessionKey, testSessionKey, "Round-tripped sessionKey must match");
    assert.strictEqual(storedOnDisk.role, "explore", "Round-tripped role must match");
    assert.strictEqual(storedOnDisk.workspace, testWorkspace, "Round-tripped workspace must match");
    assert.strictEqual(storedOnDisk.turns, 1, "Turn count must initialize to 1");
    assert.strictEqual(storedOnDisk.lastStatus, "SUCCESS", "Last status must match");
    console.log("PASS: Conversation id round-tripped through real file read.");

    // 3. Assert second buildAgyArgs call with SAME sessionKey, SAME workspace, SAME role CONTAINS --conversation <that id>
    const exploreSpec = ROLE_SPECS["explore"];
    const argv1 = buildAgyArgs({
        role: "explore",
        goal: "Second turn resuming prior context",
        workspace: testWorkspace,
        sessionKey: testSessionKey,
        sessionsPath: testStorePath,
    }, exploreSpec);

    assert.ok(argv1.includes("--conversation"), "argv1 must include --conversation flag");
    const convIdx1 = argv1.indexOf("--conversation");
    assert.strictEqual(argv1[convIdx1 + 1], fakeConvId, "argv1 --conversation value must match stored id");
    console.log("PASS: Second buildAgyArgs call with matching sessionKey, workspace, and role emitted --conversation <id>.");

    // 4. Assert that changing the workspace or role does NOT emit --conversation
    // 4a. Changing workspace
    const otherWorkspace = path.resolve(path.join(testDir, "different-workspace"));
    fs.mkdirSync(otherWorkspace, { recursive: true });
    const argv2 = buildAgyArgs({
        role: "explore",
        goal: "Attempt resumption in different workspace",
        workspace: otherWorkspace,
        sessionKey: testSessionKey,
        sessionsPath: testStorePath,
    }, exploreSpec);

    assert.ok(!argv2.includes("--conversation"), "argv2 (different workspace) must NOT include --conversation");
    console.log("PASS: Changing workspace did not emit --conversation flag.");

    // 4b. Changing role
    const reviewSpec = ROLE_SPECS["review"];
    const argv3 = buildAgyArgs({
        role: "review",
        goal: "Attempt resumption with different role",
        workspace: testWorkspace,
        sessionKey: testSessionKey,
        sessionsPath: testStorePath,
    }, reviewSpec);

    assert.ok(!argv3.includes("--conversation"), "argv3 (different role) must NOT include --conversation");
    console.log("PASS: Changing role did not emit --conversation flag.");

    // 5. Assert that omitting sessionKey emits no --conversation flag
    const argv4 = buildAgyArgs({
        role: "explore",
        goal: "Stateless call without sessionKey",
        workspace: testWorkspace,
        sessionsPath: testStorePath,
    }, exploreSpec);

    assert.ok(!argv4.includes("--conversation"), "argv4 (omitting sessionKey) must NOT include --conversation");
    console.log("PASS: Omitting sessionKey emitted no --conversation flag.");

    // 6. Assert that a deliberately corrupted store file degrades to empty state without throwing
    const corruptStorePath = path.join(testDir, "corrupt-sessions.json");
    fs.writeFileSync(corruptStorePath, "{ invalid corrupt json here :::", "utf8");
    let corruptResult = null;
    assert.doesNotThrow(() => {
        corruptResult = loadSessionStore(corruptStorePath);
    }, "loadSessionStore on corrupt file must not throw");
    assert.deepStrictEqual(corruptResult, { version: 1, sessions: {} }, "Corrupt file must degrade to empty store");
    console.log("PASS: Corrupted store file degraded to empty state without throwing.");

    // 7. Test missing file fallback
    const missingStorePath = path.join(testDir, "nonexistent-sessions.json");
    let missingResult = null;
    assert.doesNotThrow(() => {
        missingResult = loadSessionStore(missingStorePath);
    }, "loadSessionStore on missing file must not throw");
    assert.deepStrictEqual(missingResult, { version: 1, sessions: {} }, "Missing file must degrade to empty store");
    console.log("PASS: Missing store file returned empty state without throwing.");

    // 8. Test turn incrementation
    recordSessionFromEnvelope({
        sessionKey: testSessionKey,
        envelope: fakeEnvelope,
        role: "explore",
        workspace: testWorkspace,
        lastStatus: "SUCCESS",
    }, testStorePath);
    const storedTurn2 = getSession(testSessionKey, testStorePath);
    assert.strictEqual(storedTurn2.turns, 2, "Turns counter must increment to 2 on second recorded turn");
    console.log("PASS: Turn counter correctly incremented to 2.");

    // 9. Test pruning (LRU eviction + TTL)
    const pruningStore = {
        version: 1,
        sessions: {
            expired: {
                conversationId: "c-old",
                sessionKey: "expired",
                role: "explore",
                workspace: testWorkspace,
                model: "gemini-3.8-flash-high",
                effort: "high",
                timestamp: new Date(Date.now() - 100_000).toISOString(),
                turns: 1,
                lastStatus: "SUCCESS",
            },
            s1: {
                conversationId: "c-1",
                sessionKey: "s1",
                role: "explore",
                workspace: testWorkspace,
                model: "gemini-3.8-flash-high",
                effort: "high",
                timestamp: new Date(Date.now() - 50_000).toISOString(),
                turns: 1,
                lastStatus: "SUCCESS",
            },
            s2: {
                conversationId: "c-2",
                sessionKey: "s2",
                role: "explore",
                workspace: testWorkspace,
                model: "gemini-3.8-flash-high",
                effort: "high",
                timestamp: new Date(Date.now() - 10_000).toISOString(),
                turns: 1,
                lastStatus: "SUCCESS",
            },
        },
    };

    // TTL 60s -> 'expired' dropped; maxSessions: 1 -> 's1' evicted, leaving only 's2'
    const pruned = pruneSessions(pruningStore, { ttlMs: 60_000, maxSessions: 1 });
    assert.strictEqual(Boolean(pruned.sessions.expired), false, "Expired entry must be dropped by TTL");
    assert.strictEqual(Boolean(pruned.sessions.s1), false, "Oldest entry must be evicted by maxSessions limit");
    assert.strictEqual(Boolean(pruned.sessions.s2), true, "Most recently used entry must survive eviction");
    console.log("PASS: Pruning (TTL expiration and LRU eviction) verified.");

    // 10. Test listSessions and clearSessions
    const list = listSessions(testStorePath);
    assert.strictEqual(list.length, 1);
    assert.strictEqual(list[0].conversationId, fakeConvId, "Conversation ID must not be truncated in list output");

    const clearSingle = clearSessions(testSessionKey, testStorePath);
    assert.strictEqual(clearSingle.clearedCount, 1);
    assert.strictEqual(clearSingle.remainingCount, 0);

    const afterClearList = listSessions(testStorePath);
    assert.strictEqual(afterClearList.length, 0);
    console.log("PASS: listSessions and clearSessions operations verified.");

    // Print exact argv outputs for the 4 assertions
    console.log("\n=== EXACT ARGV OUTPUTS FOR THE 4 ASSERTIONS ===");
    console.log("\n[Assertion 1: same sessionKey + same workspace + same role]");
    console.log(JSON.stringify(argv1, null, 2));

    console.log("\n[Assertion 2: changing workspace (different workspace)]");
    console.log(JSON.stringify(argv2, null, 2));

    console.log("\n[Assertion 3: changing role (different role)]");
    console.log(JSON.stringify(argv3, null, 2));

    console.log("\n[Assertion 4: omitting sessionKey (stateless)]");
    console.log(JSON.stringify(argv4, null, 2));

    console.log("\n=== ALL SELF-TEST ASSERTIONS PASSED ===");
} finally {
    try {
        fs.rmSync(testDir, { recursive: true, force: true });
    } catch {
        /* ignore cleanup */
    }
}
