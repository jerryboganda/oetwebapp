# Token Saver Routing

This workspace uses `cc_token_saver_mcp` as the default first-pass router for every prompt.

Mandatory behavior:

- Always attempt to route the request through `cc-token-saver` first.
- Prefer `query_local_llm` for short, isolated generation, refactor, documentation, code review, or Q&A tasks.
- Prefer `query_local_llm_with_context` when the task needs a small amount of local context.
- Only fall back to the main model when the request needs broad repo reasoning, multi-step orchestration, or the local server is unavailable.
- Do not skip token-saver routing just because the task looks simple.

Operational notes:

- The server lives at `.claude/mcp/cc_token_saver_mcp/launch.py`.
- The server loads `.claude/mcp/cc_token_saver_mcp/.env` from the repository root.
- If the local LLM endpoint is unavailable, continue with the main model rather than blocking the task.