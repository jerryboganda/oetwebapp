import { judge } from "D:/Projects/chatgpt-jev/src/lib/judge.ts";

const state = {
  task: "Choose the safest next engineering action after the corrected OET Writing CI run failed.",
  facts: [
    "The branch compiles in the backend whole-solution gate.",
    "The frontend TypeScript check and canonical Writing rulebook regeneration stability check pass.",
    "Frontend Vitest has three failures: a medicine rulebook count expected 82 but received 84; a canonical critical-rule count expected 113 but received 122; and OA4 IDs are reported as unknown by a legacy namespace parity test.",
    "Backend Writing tests have a cluster of model-answer gate failures returning HeldForReview where tests expect Ready, one semantic validator error-key mismatch, a loader count failure, and isolated OA4_04 and minor-naming fixture failures.",
    "The latest commit only aligned two named test arguments and did not change production behavior.",
    "No production mutation has occurred."
  ]
};

const questions = {
  next_action: {
    type: "choice",
    instructions: "Which alternative best fits the evidence and safest next action? Use only the stated facts; do not invent missing test details.",
    criteria: {
      fix_shared_behavior: "Multiple backend failures share a HeldForReview or validation behavior and should be traced to the common implementation before changing expectations.",
      update_stale_expectations: "The failures are intentional additions or renamed expectations, with no evidence of a shared production regression.",
      split_and_pause: "The failures span independent clusters and should be investigated separately before any release claim or broad patch."
    }
  }
};

try {
  const answers = await judge("oet_writing_ci_failure_triage", state, questions, { timeoutMs: 8000 });
  const answer = answers?.next_action;
  if (!answer || typeof answer.choice !== "string" || !Object.hasOwn(questions.next_action.criteria, answer.choice) || !answer.probabilities || Object.values(answer.probabilities).some((value) => typeof value !== "number" || !Number.isFinite(value))) {
    throw new Error("Invalid typed Jev answer");
  }
  console.log(JSON.stringify({ answer }));
} catch (error) {
  console.error(JSON.stringify({ error: error instanceof Error ? error.message : String(error) }));
  process.exitCode = 3;
}
