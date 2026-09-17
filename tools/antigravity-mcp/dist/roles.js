import { PINNED_EFFORT, PINNED_MODEL } from "./security.js";
export const ROLE_SPECS = {
    explore: {
        role: "explore",
        kind: "read",
        timeoutMs: 120_000,
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    research: {
        role: "research",
        kind: "read",
        timeoutMs: 180_000,
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    review: {
        role: "review",
        kind: "read",
        timeoutMs: 300_000,
        mode: "plan",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    implement: {
        role: "implement",
        kind: "write",
        timeoutMs: 300_000,
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: true,
    },
    test: {
        role: "test",
        kind: "write",
        timeoutMs: 240_000,
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: false,
    },
    debug: {
        role: "debug",
        kind: "write",
        timeoutMs: 240_000,
        mode: "accept-edits",
        sandbox: false,
        highAutonomyOptIn: false,
    },
};
export function rolePrompt(input) {
    const shared = [
        `You are an Antigravity CLI worker. Role: ${input.role}.`,
        `Pinned model: ${PINNED_MODEL}. Effort: ${PINNED_EFFORT}.`,
        `Workspace: ${input.workspace}`,
        "Billing: use only the signed-in Google / Antigravity account quota. Never request an API key.",
        "Return ONLY one JSON object matching the worker-result schema. No markdown, no preamble.",
        "JSON keys: status, role, summary, evidence, filesRead, filesChanged, commandsRun, tests, risks, blockers, recommendedNextStep, confidence.",
        "status must be SUCCESS, PARTIAL, BLOCKED, or ERROR.",
        "confidence must be high, medium, or low.",
        "Do not invent files. Evidence must cite real paths.",
        input.goal ? `Goal: ${input.goal}` : "",
        input.context ? `Context: ${input.context}` : "",
        input.extraConstraints ? `Constraints: ${input.extraConstraints}` : "",
    ].filter(Boolean);
    const roleExtra = {
        explore: "Read-only. Do not edit files or run mutating commands. Map files, ownership, and the smallest next step.",
        research: "Read-only. Investigate APIs/docs already in the workspace. Do not implement.",
        implement: "Apply the smallest safe edit that fully solves the goal. Stay inside the workspace. Do not commit unless asked. Do not touch secrets.",
        test: "Run the smallest relevant check for the change. Record command, exit code, and result. Do not expand to full-suite.",
        debug: "Reproduce, isolate root cause, apply the smallest safe fix. Do not patch symptoms in multiple callers if one shared guard exists.",
        review: "Read-only review. Report correctness, security, and contract issues. Do not edit.",
    };
    return [...shared, roleExtra[input.role]].join("\n");
}
