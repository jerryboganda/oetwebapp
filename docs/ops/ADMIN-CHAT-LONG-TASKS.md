# Admin chatbot task reliability

Release: `c47c016a9945061af309ab6875068a7b490ced90`, deployed 2026-10-08 (Pakistan time).

## Confirmed failure

Production admin assistant transport GET requests returned HTTP 429 at 22:29 UTC on October 7, followed by rejected reconnect negotiations. The browser uses SignalR long polling through the application proxy. Every poll consumed the shared HubConnect allowance of 30 requests per minute, so a response containing many events could exhaust it.

## Changes

- Assistant transport requests now have a separate bounded allowance of 600 requests per minute per authenticated user. Negotiation retains its existing cap; other hubs retain their existing policy.
- Token renewal keeps the account-scoped connection alive. Account/role changes invalidate it, and conversation events are filtered by thread identity.
- Client and server reject overlapping turns. Conversation switching is blocked during a running turn.
- Tool intent and encrypted provider state are saved before execution. Each completed tool result is saved before its event is sent, including when cancellation arrives after execution.
- Interrupted histories close unmatched tool calls explicitly and omit orphan or duplicate results. Unknown outcomes require inspection and confirmation; requests and executed tools are never automatically replayed.
- Provider body reads are asynchronous with a ten-minute deadline. Tool results supplied as model context are bounded to 12,000 characters each; stored results remain intact.
- Reconnection reloads saved history. A late invocation failure cannot replace an already completed answer with a send error.

Role permissions, learner study-only access, provider routing, reasoning metadata, quotas, approvals and iteration limits remain enforced. A real connection loss can interrupt an active turn; saved history supports an explicit continuation. Provider failures remain possible and are not hidden by automatic fallback.

## Deployment evidence

- [Image build 37697195438](https://github.com/jerryboganda/oetwebapp/actions/runs/37697195438): success.
- [Production deployment 37697783219](https://github.com/jerryboganda/oetwebapp/actions/runs/37697783219): success.
- Active slot: `blue`. Web health and API ready/live endpoints returned HTTP 200 with the exact release SHA.
- Serving web image: `sha256:d0b4eddfccdd0d542205ec50c4cb72198e70c5112707f2a5cfaf7441e78e8703`.
- Serving API image: `sha256:52b004fa2d8de277a2d394c7db309b81e75e65c11d21ab842454704c7b27eab7`.
- Separate Speaking Module CI `37697195500` failed its pending-model-changes check. This chatbot release changes no entity model or migration; production migration application and readiness checks succeeded. Model drift remains a separate unresolved repository check.

## Manual acceptance

### Follow-up: ten-step task cutoff

The owner reported the exact exhaustion response generated when the configured iteration count is reached. Admin turns now continue within the same request rather than requiring a second user message at ten rounds. They retain the original goal, shortened progress excerpts and recent complete tool groups in bounded model context; full database history is preserved. Completed tools are not automatically replayed.

Admin tasks stop normally when the model returns a final answer. A separate runaway budget of thirty minutes (checked between tool rounds) or 1,000 rounds requests a tool-free summary of actual results and unfinished work. A provider that ignores this restriction is refused further tool execution. Cancellation, approvals, quota/provider failures and genuine blockers can still stop a task. This budget replaces the short-turn iteration setting for admins only; other roles retain their existing limits.

Follow-up release: `0482ca9bfe86ee57dbe5c97a2e1bb0bee5ef73d8` blue, includes fix `65742ee15`. Build images `37703188463` and Deploy production `37703776299` succeeded. Exact serving images and web/API ready/live HTTP 200 verified on October 8. Web image `sha256:866c1cad9abbd53f5061c5424fe54ec2b772511422598b5895f47418b8061192`; API image `sha256:e3717acb4f3da49cb0779cc747a7f5286e39c21ec5905489d752b506c30b065c`. Manual acceptance must include a task needing more than ten tool rounds, a short task, cancellation, sufficient progress context after compaction, and an honest budget-exhaustion summary.

Functional acceptance: **not tested—owner QA**. No automated functional QA was run.

| Scenario | Expected result |
| --- | --- |
| Short admin question | One visible response; no failed-send banner after completion |
| Long task with multiple tool calls | Tool progress and final answer appear; assistant transport avoids the former 30/min bucket |
| Access-token renewal during a task | Connection and active turn remain intact |
| Double send or conversation switch during a task | No overlapping turn or cross-thread events |
| Cancel before or after a completed tool | Cancellation is honest; completed results remain saved |
| Connection loss and reconnection | Saved history reloads; no automatic request or tool replay |
| Resume a history with an unmatched tool call | Missing outcome is explicit; inspect before repeating an operation |
| Provider failure, malformed call or iteration exhaustion | Explicit bounded failure/continuation; no silent fallback |
| Admin, expert and learner tools | Existing role guards remain effective; learner cannot access the codebase |
