#!/usr/bin/env node
import assert from "node:assert";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { PINNED_MODEL, PINNED_EFFORT } from "./security.js";
import {
    ROLE_SPECS,
    ROLE_ROUTING,
    DEFAULT_ROLE_ROUTING,
    EXAMPLE_ROUTING_PRESET,
    resolveRoleRouting,
    getRoleRouting,
} from "./config.js";
import { buildAgyArgs, resolveRoleModelAndEffort } from "./agy-runner.js";

console.log("=== RUNNING AGY MODEL & EFFORT ROUTING SELF-TEST ===");

const testWorkspace = path.resolve("C:\\mock\\test-workspace");
const ALL_ROLES = ["explore", "research", "review", "implement", "test", "debug"];

// Helper to inspect flag occurrences and values
function assertFlags(argv, expectedModel, expectedEffort, label) {
    const modelMatches = argv.filter((a) => a === "--model");
    const effortMatches = argv.filter((a) => a === "--effort");

    assert.strictEqual(
        modelMatches.length,
        1,
        `[${label}] --model must appear exactly once in argv (found ${modelMatches.length})`
    );
    assert.strictEqual(
        effortMatches.length,
        1,
        `[${label}] --effort must appear exactly once in argv (found ${effortMatches.length})`
    );

    const modelIdx = argv.indexOf("--model");
    const effortIdx = argv.indexOf("--effort");

    const actualModel = argv[modelIdx + 1];
    const actualEffort = argv[effortIdx + 1];

    assert.ok(
        typeof actualModel === "string" && actualModel.trim().length > 0,
        `[${label}] --model value must be a non-empty string (got: ${JSON.stringify(actualModel)})`
    );
    assert.ok(
        typeof actualEffort === "string" && actualEffort.trim().length > 0,
        `[${label}] --effort value must be a non-empty string (got: ${JSON.stringify(actualEffort)})`
    );

    assert.strictEqual(
        actualModel,
        expectedModel,
        `[${label}] Expected --model to be '${expectedModel}', got '${actualModel}'`
    );
    assert.strictEqual(
        actualEffort,
        expectedEffort,
        `[${label}] Expected --effort to be '${expectedEffort}', got '${actualEffort}'`
    );
}

// =========================================================================
// 1. Assert configuration knobs, defaults, and example preset in config.js
// =========================================================================
console.log("\n--- Checking dist/config.js exports, defaults, and example preset ---");
assert.ok(DEFAULT_ROLE_ROUTING, "DEFAULT_ROLE_ROUTING must be defined");
assert.strictEqual(typeof DEFAULT_ROLE_ROUTING, "object");
assert.strictEqual(Object.keys(DEFAULT_ROLE_ROUTING).length, 0, "DEFAULT_ROLE_ROUTING must default to empty object");

assert.ok(ROLE_ROUTING, "ROLE_ROUTING must be defined");
assert.strictEqual(typeof ROLE_ROUTING, "object");
assert.strictEqual(Object.keys(ROLE_ROUTING).length, 0, "ROLE_ROUTING must default to empty object when unconfigured");

assert.ok(EXAMPLE_ROUTING_PRESET, "EXAMPLE_ROUTING_PRESET must be defined");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.explore.model, "gemini-3.8-flash-low");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.explore.effort, "low");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.research.model, "gemini-3.8-flash-medium");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.research.effort, "medium");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.review.model, "gemini-3.8-flash-high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.review.effort, "high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.implement.model, "gemini-3.8-flash-high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.implement.effort, "high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.test.model, "gemini-3.8-flash-high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.test.effort, "high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.debug.model, "gemini-3.8-flash-high");
assert.strictEqual(EXAMPLE_ROUTING_PRESET.debug.effort, "high");

// Test resolveRoleRouting with various tolerant structures
const resolvedFromRouting = resolveRoleRouting({
    routing: { explore: { model: "gemini-3.8-flash-low", effort: "low" } },
});
assert.deepStrictEqual(resolvedFromRouting.explore, { model: "gemini-3.8-flash-low", effort: "low" });

const resolvedFromRoles = resolveRoleRouting({
    roles: { explore: { model: "gemini-3.8-flash-low", timeoutMs: 300000 } },
});
assert.deepStrictEqual(resolvedFromRoles.explore, { model: "gemini-3.8-flash-low" });

const resolvedFromFlat = resolveRoleRouting({
    research: { model: "gemini-3.8-flash-medium", effort: "medium" },
});
assert.deepStrictEqual(resolvedFromFlat.research, { model: "gemini-3.8-flash-medium", effort: "medium" });

console.log("PASS: dist/config.js exports, defaults, and tolerant resolution verified.");

// =========================================================================
// 2. CRITICAL NO-REGRESSION ASSERTION: With NO routing configured,
//    buildAgyArgs emits EXACTLY --model gemini-3.8-flash-high and
//    --effort high for EVERY role.
// =========================================================================
console.log("\n--- Assertion 1: No routing configured (critical no-regression check) ---");
const defaultArgvs = {};
for (const role of ALL_ROLES) {
    const spec = ROLE_SPECS[role];
    const argv = buildAgyArgs({
        role,
        workspace: testWorkspace,
        goal: `Verify default behavior for ${role}`,
    }, spec);

    defaultArgvs[role] = argv;
    assertFlags(argv, PINNED_MODEL, PINNED_EFFORT, `Default (no routing) - role: ${role}`);
    console.log(`PASS: Role '${role}' emits --model ${PINNED_MODEL} --effort ${PINNED_EFFORT}`);
}

// =========================================================================
// 3. Routing override configured for a role:
//    The emitted argv uses the override for that role while all
//    unconfigured roles still use pinned defaults.
// =========================================================================
console.log("\n--- Assertion 2: Role routing override applied to target role, defaults preserved on others ---");
const customRouting = {
    explore: { model: "gemini-3.8-flash-low", effort: "low" },
};

// 3a. Test target role with override passed as 3rd arg
const exploreArgvOverridden = buildAgyArgs({
    role: "explore",
    workspace: testWorkspace,
    goal: "Verify routing override for explore",
}, ROLE_SPECS.explore, customRouting);

assertFlags(exploreArgvOverridden, "gemini-3.8-flash-low", "low", "Override 3rd arg - explore");
console.log("PASS: Role 'explore' with override emits --model gemini-3.8-flash-low --effort low");

// 3b. Test target role with override passed via input.routing
const exploreArgvInputRouting = buildAgyArgs({
    role: "explore",
    workspace: testWorkspace,
    goal: "Verify input.routing for explore",
    routing: customRouting,
}, ROLE_SPECS.explore);

assertFlags(exploreArgvInputRouting, "gemini-3.8-flash-low", "low", "input.routing - explore");
console.log("PASS: Role 'explore' with input.routing emits --model gemini-3.8-flash-low --effort low");

// 3c. Assert that all other UNCONFIGURED roles continue using pinned defaults
for (const role of ALL_ROLES) {
    if (role === "explore") continue;
    const spec = ROLE_SPECS[role];
    const argv = buildAgyArgs({
        role,
        workspace: testWorkspace,
        goal: `Verify unconfigured role ${role} remains on pinned defaults`,
    }, spec, customRouting);

    assertFlags(argv, PINNED_MODEL, PINNED_EFFORT, `Unconfigured role with explore overridden - ${role}`);
    console.log(`PASS: Unconfigured role '${role}' correctly remains on --model ${PINNED_MODEL} --effort ${PINNED_EFFORT}`);
}

// 3d. Test partial overrides (model only or effort only)
console.log("\n--- Assertion 3: Partial routing overrides (model-only or effort-only) ---");
const modelOnlyRouting = {
    research: { model: "gemini-3.8-flash-medium" },
};
const researchArgv = buildAgyArgs({
    role: "research",
    workspace: testWorkspace,
    goal: "Verify model-only override",
}, ROLE_SPECS.research, modelOnlyRouting);
assertFlags(researchArgv, "gemini-3.8-flash-medium", PINNED_EFFORT, "Model-only override - research");
console.log(`PASS: Model-only override resolved model='gemini-3.8-flash-medium' and effort='${PINNED_EFFORT}'`);

const effortOnlyRouting = {
    test: { effort: "medium" },
};
const testArgv = buildAgyArgs({
    role: "test",
    workspace: testWorkspace,
    goal: "Verify effort-only override",
}, ROLE_SPECS.test, effortOnlyRouting);
assertFlags(testArgv, PINNED_MODEL, "medium", "Effort-only override - test");
console.log(`PASS: Effort-only override resolved model='${PINNED_MODEL}' and effort='medium'`);

// =========================================================================
// 4. Malformed routing config does not throw and silently falls back to pinned defaults
// =========================================================================
console.log("\n--- Assertion 4: Malformed routing config does not throw and falls back silently ---");
const malformedCases = [
    { label: "null", config: null },
    { label: "undefined", config: undefined },
    { label: "string", config: "not-a-valid-config" },
    { label: "number", config: 42 },
    { label: "boolean", config: true },
    { label: "array", config: [{ explore: { model: "foo" } }] },
    { label: "role entry is null", config: { explore: null } },
    { label: "role entry is string", config: { explore: "invalid-string" } },
    { label: "role entry is number", config: { explore: 12345 } },
    { label: "empty model and whitespace effort", config: { explore: { model: "", effort: "   " } } },
    { label: "null model and array effort", config: { explore: { model: null, effort: [] } } },
    { label: "numeric model and object effort", config: { explore: { model: 12345, effort: {} } } },
];

for (const { label, config } of malformedCases) {
    let argv;
    assert.doesNotThrow(() => {
        argv = buildAgyArgs({
            role: "explore",
            workspace: testWorkspace,
            goal: `Testing malformed case: ${label}`,
        }, ROLE_SPECS.explore, config);
    }, `buildAgyArgs must not throw for malformed config: ${label}`);

    assertFlags(argv, PINNED_MODEL, PINNED_EFFORT, `Malformed config fallback - ${label}`);
    console.log(`PASS: Malformed config '${label}' silently fell back to pinned defaults without throwing.`);
}

// Test resolveRoleRouting with malformed inputs directly
for (const { label, config } of malformedCases) {
    let res;
    assert.doesNotThrow(() => {
        res = resolveRoleRouting(config);
    }, `resolveRoleRouting must not throw for malformed config: ${label}`);
    assert.strictEqual(typeof res, "object", `resolveRoleRouting must return an object for ${label}`);
}
console.log("PASS: resolveRoleRouting tolerates all malformed inputs without throwing.");

// =========================================================================
// 5. Assert --model and --effort each appear exactly once in all generated argvs
// =========================================================================
console.log("\n--- Assertion 5: Exactly-once assertion across all configurations ---");
const testArgvsToCheck = [
    ...Object.values(defaultArgvs),
    exploreArgvOverridden,
    exploreArgvInputRouting,
    researchArgv,
    testArgv,
];
for (let i = 0; i < testArgvsToCheck.length; i++) {
    const argv = testArgvsToCheck[i];
    const modelCount = argv.filter((a) => a === "--model").length;
    const effortCount = argv.filter((a) => a === "--effort").length;
    assert.strictEqual(modelCount, 1, `argv #${i} must have exactly one --model flag`);
    assert.strictEqual(effortCount, 1, `argv #${i} must have exactly one --effort flag`);
}
console.log(`PASS: Verified exactly-once constraint on ${testArgvsToCheck.length} generated argv arrays.`);

// =========================================================================
// EXACT ARGV PRINTOUT FOR REPORTING
// =========================================================================
console.log("\n=== EXACT ARGV OUTPUT FOR ROLE 'explore' WITHOUT OVERRIDE ===");
console.log(JSON.stringify(defaultArgvs.explore, null, 2));

console.log("\n=== EXACT ARGV OUTPUT FOR ROLE 'explore' WITH OVERRIDE ===");
console.log(JSON.stringify(exploreArgvOverridden, null, 2));

console.log("\n=== ALL ROUTING SELF-TEST ASSERTIONS PASSED SUCCESSFULLY ===");
