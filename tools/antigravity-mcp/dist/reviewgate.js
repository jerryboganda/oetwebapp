import { resolveReviewGate } from "./config.js";

// Adversarial review gate (OPT-IN, default OFF).
// RECURSION SAFETY INVARIANT: only roles whose ROLE_SPECS kind is "write" are gated, and the
// reviewer role is skipped explicitly. The reviewer is a read-kind role, so it takes the read
// path in agy-runner.js, which this gate never wraps - a review can never recurse.

export function buildReviewPrompt(r, wt) {
    return [
        "You are an ADVERSARIAL REVIEWER. Find every reason the work below is WRONG or INCOMPLETE.",
        "Do NOT be agreeable. Look for unmet requirements, fabricated evidence, files claimed",
        "changed but not, tests claimed but never run, and unhandled edge cases.",
        "Inspect the real files in the workspace before deciding, and cite concrete evidence.",
        "",
        "WORK UNDER REVIEW",
        "Role: " + String(r && r.role),
        "Summary: " + String(r && r.summary),
        "Files changed: " + JSON.stringify((r && r.filesChanged) || []),
        "Evidence: " + JSON.stringify((r && r.evidence) || []).slice(0, 4000),
        "Tests claimed: " + JSON.stringify((r && r.tests) || []).slice(0, 2000),
        "Workspace to inspect: " + (wt || "(main workspace)"),
        "",
        "End your response with EXACTLY one final line:",
        "VERDICT: PASS",
        "or",
        "VERDICT: FAIL: <concise reason>",
    ].join(String.fromCharCode(10));
}

export function parseVerdict(text) {
    const s = String(text || "");
    const m = s.match(/VERDICT:\s*(PASS|FAIL)\s*:?\s*([\s\S]*)/i);
    if (!m) return { verdict: "UNKNOWN", summary: s.trim().slice(0, 500) };
    return { verdict: m[1].toUpperCase(), summary: String(m[2] || "").trim().slice(0, 1000) };
}

export async function applyReviewGate(result, input, spec, wt, runWorkerFn) {
    const cfg = resolveReviewGate(input && input.reviewGate);
    if (!cfg.enabled || !spec || spec.kind !== "write") return result;
    if (!result || result.status !== "SUCCESS") return result;
    if (input && input.role === cfg.reviewerRole) return result;
    if (typeof runWorkerFn !== "function") return result;
    const base = { reviewerRole: cfg.reviewerRole, reviewedRole: result.role, worktreePath: wt || null };
    let rr;
    try {
        rr = await runWorkerFn({
            role: cfg.reviewerRole,
            workspace: wt || input.workspace,
            goal: buildReviewPrompt(result, wt),
            context: "Adversarial verification pass required by the review gate. Read-only.",
            timeoutMs: cfg.maxReviewBudgetMs,
        });
    } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.warn("[agy-reviewgate] reviewer failed: " + msg);
        const v = Object.assign({}, base, { verdict: "UNKNOWN", error: msg });
        if (!cfg.failClosed) return Object.assign({}, result, { reviewVerdict: v });
        return Object.assign({}, result, {
            status: "BLOCKED",
            reviewVerdict: v,
            blockers: (result.blockers || []).concat(["REVIEW_GATE_REVIEWER_FAILED"]),
            summary: result.summary + " | REVIEW GATE: reviewer failed (" + msg + "); failClosed on, so BLOCKED rather than trusted.",
        });
    }
    const parsed = parseVerdict(rr && rr.summary);
    console.warn("[agy-reviewgate] " + result.role + " verdict: " + parsed.verdict);
    const verdict = Object.assign({}, base, parsed);
    if (parsed.verdict !== "PASS" && cfg.failClosed) {
        return Object.assign({}, result, {
            status: "BLOCKED",
            reviewVerdict: verdict,
            blockers: (result.blockers || []).concat(["REVIEW_GATE_FAILED"]),
            summary: result.summary + " | REVIEW GATE FAILED (" + parsed.verdict + "): " + parsed.summary,
        });
    }
    return Object.assign({}, result, { reviewVerdict: verdict });
}

