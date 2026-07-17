---
name: acreadiness-assess
description: Use when the user asks for an AI-readiness assessment, a readiness check, an audit, or wants to see how AI-ready their repository is.
---

# /acreadiness-assess — AI-readiness assessment

Use this skill whenever the user asks for an **AI-readiness assessment**, a **readiness check**, an **audit**, or wants to **see how AI-ready** their repository is.

This skill is the *Measure* step in AgentRC's **Measure → Generate → Maintain** loop. The result is a self-contained HTML dashboard the user can open with `file://` or commit to the repo.

## Steps

1. **Confirm prerequisites.** Node 20+ must be on PATH. If unsure, run `node --version`.
2. **Decide on a policy** (optional but encouraged):
   - If the user provided `--policy <source>`, capture it.
   - Otherwise check `agentrc.config.json` for a `policies` array.
   - If neither, run with no policy (built-in defaults).
3. **Run the readiness scan** in the repo root with structured output:
   ```bash
   npx -y github:microsoft/agentrc readiness --json [--policy <source>] [--per-area]
   ```
   The `CommandResult<T>` JSON envelope is your input for the next step.
4. **Hand off to the `ai-readiness-reporter` custom agent** to interpret the JSON and produce `reports/index.html`. The agent renders via the bundled template `report-template.html` (shipped alongside this skill) so every report has an identical look & feel.
5. **Tell the user where the report lives** (`reports/index.html`) and how to open it. Summarise in chat: maturity level, overall score, top three lowest pillars, and the single highest-leverage next action.

## Notes

- AgentRC also has a built-in HTML renderer (`--visual` / `--output report.html`) but its output is intentionally generic.
- For CI gating, recommend `agentrc readiness --fail-level <n>` (1–5).
- The skill never modifies repository files other than creating `reports/index.html`.
