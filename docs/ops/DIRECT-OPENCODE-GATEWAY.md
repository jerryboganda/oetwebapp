# Direct OpenCode gateway

Owner-approved replacement, 2026-10-08. Functional acceptance: **not tested—owner QA**.

Admin chat, learner chat and the owner agent console share the active, encrypted
`opencode` provider row managed at `/admin/ai-providers`. Inference is an HTTPS
chat-completions call from the application API to the configured OpenCode gateway.
No OpenCode executable, SDK, local server or inference container is packaged in
the console. Its existing service retains sessions, worktrees, Guard, approvals,
snapshots, taint handling, egress/Docker restrictions, cancellation and Ship.

The learner default remains `deepseek-v4.1-flash`, effort `max`; admin and console
defaults stay as configured. OpenCode is selectable and is labeled **Direct
OpenCode gateway**. Discovery requires an active provider, a decryptable key,
an approved HTTPS gateway URL and an allowed model from the curated catalogue.

## Credential and state boundaries

- Provider keys stay encrypted in the backend database. Console tools, child
  environments, browser responses and transcripts receive no provider key.
- Assistant reasoning metadata is encrypted with ASP.NET Data Protection and
  bound to the conversation id. The additive `EncryptedProviderState` column is
  absent from public message DTOs. DeepSeek replays decrypted `reasoning_content`
  only inside provider payloads, including tool continuations.
- Console control calls use the existing internal token and owner allowlist over
  `oet_agent_ctl` through the API router. `/internal/owner-agent/opencode/status`
  reports readiness; `/completions` emits heartbeat, text and completion/error
  events. Completion state is opaque ciphertext, stored in root-only
  `/var/lib/oet-agent/sessions/gateway/<session-id>.json`, never public events.
- Each inference checks a running OpenCode session and an active owner lease
  (bounded by the backend unlock), plus the kill switch. Every tool is separately
  checked by the existing Guard/approval hook and runs as UID `10002`.
- `Read`, `Write`, `Edit` and `Bash` have bounded arguments/output. File tools
  refuse paths and symlinks escaping the session worktree. Cancellations/timeouts
  kill tool process groups. Shell commands remain subject to existing policy.

## Continuation and failures

Historical visible transcripts and worktrees are preserved. Missing legacy
reasoning metadata starts a new provider context from a bounded reference
summary. Obsolete console native model ids normalize to the shared default.
Historical home volumes remain mounted but native OpenCode auth/session stores
are no longer read, written or pruned by a CLI.

The console persists complete tool-call intent before execution and each result
after execution. Interrupted or unknown outcomes are closed explicitly on resume;
they are never automatically executed again. Inspect the worktree before an
explicit retry. Malformed calls, unmatched results, interrupted streams and the
24-step limit stop the turn with an actionable error. There is no provider
fallback. Learners retain the specified busy message; admins/owners receive
sanitized gateway errors. Earlier provider-error records do not prove a cause.

## Release and owner acceptance

Release the backend compatibility/migration/router network first through Build
images → Deploy production. Then release the console/UI through the existing
application and Owner Agent Console workflows. Active-turn draining applies.
Compilation is verified by image builds; no automated QA runs locally or in CI.
Record serving images, migration presence, health, gateway readiness and absence
of native OpenCode runtime paths after deployment.

Owner manual acceptance remains required for:

| Scenario | Expected result |
| --- | --- |
| Select OpenCode on all three surfaces | Same label; allowed models; existing defaults retained |
| Learner study-plan preview and confirmation | Preview first; mutation only after learner confirmation |
| Admin tools | Existing role/tool permissions, grounding and usage accounting |
| Console Read/Edit/Bash, approve and deny | Guard and approvals govern every tool; denied tools do not execute |
| Interrupt, timeout, stop and resume | Work remains visible; no completed/unknown operation is replayed automatically |
| Continue legacy session | Worktree/transcript retained; summary and valid gateway model used |
| Disabled/missing credential or disallowed model | Unavailable readiness; sanitized error; no fallback |
| Provider failure/malformed or truncated stream | Explicit stop; no tool executes from incomplete arguments |

Health/readiness checks and successful builds are deployment evidence; functional
acceptance stays **not tested—owner QA** until the owner confirms these scenarios.

## Serving evidence, 2026-10-08

| Release | Evidence |
| --- | --- |
| Backend compatibility | `8956f7f489aee1aa2c4b78b9f3b9bc9b9bc784f5`; Build images `37678854057`; Deploy production `37679851932`; migration applied before replacement |
| Application replacement | `4669e7d5cd7a71f63cbe16dc38cd0a58d8b789fe`; Build images `37684748782`; Deploy production `37685674010`; healthy green slot, matching release headers and serving-image proof |
| Console replacement | `a80ae47acd59ec980de0afc3234f5a9d91304b12`; Owner Agent Console workflow `37686649325`, success; healthy container and running adapter readiness inspected |
| Current application descendant | `054940824debed241bf5bb8d5798b01b95ed4f1e`; Build images `37687165739`; Deploy production `37687807816`; healthy blue slot with gateway changes retained and exact serving proof |

Serving digests:

- Web: `sha256:f64548aac8a85654be87e09b0bbfc9fd42a319c3a1644b1fe23b6fd750316af1`.
- Current API: `sha256:988957e5d0eef52a12656d99080c626c09d035f4220d942a7fa12d8c8f07f179` (initial replacement: `sha256:3daa3c8222f712b150d92254b2545da0b9a26319ffadf7e37245a2f83b74a897`).
- Console: `sha256:f2aab41ca8a3cb78bef171af59d04775d518ac22d6874116a4e3cdb3cf42fa8f`.

Read-only production inspection confirmed shared readiness, the encrypted-state
migration, learner `opencode/deepseek-v4.1-flash` and admin
`anthropic/claude-sonnet-5` defaults. The running console reports
`version=direct-gateway`, configured effort `max`, and no native executable,
OpenCode package/SDK/protocol path, server-spawn path or OpenCode process name.
All three historical console volumes remain mounted.

The application build's NuGet setup race was removed by sequential restore/tool
installation. Legacy automated regression jobs were disabled; the final build
skipped both. Console pulls repeatedly reset over IPv6; the existing workflow's
temporary IPv4 blob-host retry succeeded, and its `/etc/hosts` marker was confirmed
absent after rollout. No daemon/network configuration was changed.

No inference/tool/UI acceptance was run. The manual matrix above is still
**not tested—owner QA**. Historical provider errors have no established root cause.

## Learner policy override, 2026-10-08

Owner directive supersedes the learner picker/default policy above: learners can only use `deepseek-v4.1-flash` through the direct gateway. Claude and UBAG remain available to admins, but are rejected for learner selection. Every learner inference ignores historical provider/model pins and uses the approved model. The learner panel is branded **OET Personal Ai Assistant**, with no model/provider selector or visible provider identifiers.

Production logs identified the reported unavailable response before inference: DirectAiCallRecorder operation insertion failed with SQLSTATE 22001 (varchar(64)). The concatenated feature/user/timestamp resource ID overflowed for long user IDs. Resource IDs are now GUIDs; request hashes are SHA-256. Accounting and provider refusal controls are retained.

The learner-safe tool allowlist continues to filter discovery and execution. No filesystem, codebase, shell or deployment tools are available. The learner prompt explicitly limits assistance to OET/English study and authorized study material/plans, including prompt fallback.

Release `f7ae11a35d5cfebf18f46313ae8c21cd025f9166`: Build images `37692008180`, Deploy production `37692695925`, both success. Serving headers and router/image proof match the release in blue. Web digest `sha256:291c3574f5e61d932513cec11a511169faa7d7d643620f457fa7983f45a56c14`; API digest `sha256:2fba8b1d0b75bdf840fdbd4dbcccb5b051fe65f50ca9f606e527c5a2abad3d4a`.

Owner manual QA: a learner receives an OET study reply; old Claude/UBAG/GLM pins use the approved assistant; explicit unsupported model updates fail; provider/model names are hidden; authorized materials work; codebase/tool and off-topic requests are refused. Functional acceptance **not tested—owner QA**.
