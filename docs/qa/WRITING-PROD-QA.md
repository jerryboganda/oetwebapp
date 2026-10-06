# Writing production QA harness (WAI-10)

Live QA of the Writing AI launch against **production**: fresh disposable learners type the 36 verbatim
handoff letters into the real Writing tasks, the P0-3 acceptance scenarios run, every result page gets
geometry/overlap checks, and the QA-2 evidence table is published. Built for the launch checklist rows
1-15 (plan `implement-100-leave-twinkling-catmull.md` §5 WAI-10, §9).

| File | Role |
| --- | --- |
| `.github/workflows/writing-prod-qa.yml` | The only way to run it (manual dispatch) |
| `scripts/qa/writing-prod-qa/contract.mjs` | **Every** host, API path, route, provider id, flag key, selector and test id |
| `scripts/qa/writing-prod-qa/scripts.json` | The 36 handoff scripts, verbatim (plan Appendix A) |
| `scripts/qa/writing-prod-qa/lib.mjs` | Pure verdicts: scripts, inputs, discovery plan, credits, timer, provider evidence, preflight/guard, lane, table |
| `scripts/qa/writing-prod-qa/geometry.mjs` | In-page collectors + pure geometry/overlap detectors |
| `scripts/qa/writing-prod-qa/api.mjs` | Admin HTTP client (one per run), discovery reads, provisioning, flags, cleanup |
| `scripts/qa/writing-prod-qa/browser.mjs` | Playwright journey (sign-in, library, typing, submit, results, Post Submissions) |
| `scripts/qa/writing-prod-qa/run.mjs` | Orchestrator: `check-inputs`, `run`, `safety-net` |
| `scripts/qa/writing-prod-qa/*.test.ts` | Vitest: pure logic, read-only client, workflow structure |
| `tests/e2e/learner/writing-qa-detectors.spec.ts` | Hermetic Playwright spec: the detectors flag bad layouts (`page.setContent`) |

## How to dispatch

`workflow_dispatch` only works for a workflow that is on `main`; once it is, edits can be dispatched from a
branch with `--ref <branch>`.

```bash
gh workflow run writing-prod-qa.yml --ref main -f suite=discover                     # read-only plan (default)
gh workflow run writing-prod-qa.yml --ref main -f suite=acceptance                   # P0-3 S1-S8, reading resume, L1+L2 fault
gh workflow run writing-prod-qa.yml --ref main -f suite=ui -f browsers=chromium,webkit
gh workflow run writing-prod-qa.yml --ref main -f suite=matrix -f concurrency=3
gh workflow run writing-prod-qa.yml --ref main -f suite=all
gh workflow run writing-prod-qa.yml --ref main -f suite=letters -f cleanup=always   # 6 realistic mixed major/minor letters (not part of all)
gh workflow run writing-prod-qa.yml --ref main -f suite=matrix -f professions=nursing -f categories=urgent   # one cell
```

## Inputs

| Input | Default | Meaning |
| --- | --- | --- |
| `suite` | `discover` | `discover` (read-only plan) · `matrix` · `acceptance` · `ui` · `letters` · `all` (`letters` is not part of `all`) |
| `professions` | all | Comma-separated handoff ids; disabled ones are still listed as `NOT_ENABLED` |
| `categories` | all | `routine,urgent,discharge` |
| `reading_window` | `real` | `real` = live 5-minute window; `seed` = page clock fast-forward (noted on every row). Acceptance is always real |
| `concurrency` | `3` | Learner workers typing in parallel; grading is one letter at a time |
| `pace_seconds` | `35` | Min seconds between two grading calls of one learner (AiScoring limiter: 2/min per user); >= 35 |
| `fault_mode` | `flag` | `flag` = WAI-05 fault flags (real server failure); `client` = fake the failed status in the page (S6-S8 then `PARTIAL`); `none` = skip |
| `verify_credits` | `true` | Assert the credit rule per letter |
| `require_l2_disabled` | `false` | Refuse unless the `anthropic` (L2, paid) provider row is inactive |
| `preflight_repair` | `false` | Reset an open/stale **L2/L3** provider circuit before starting (audited admin call); never the Max circuit, never the quota marker |
| `browsers` | `chromium` | `ui` suite devices: Pixel 7, plus iPhone 14 with `chromium,webkit` |
| `cleanup` | `on_success` | Purge the disposable learners only when every test passed; `always`; `never` |

There is deliberately **no provider-mode input**: the Claude Max subscription route is never switched off or
skipped (owner rule; the admin API refuses `mode=codex`).

## Secrets

Only `OET_ADMIN_EMAIL` / `OET_ADMIN_PASSWORD` (the production admin login the existing workflows use) plus the
job's own `github.token` (deploy check). The admin sign-in signs that admin out of every other session; a
dedicated QA admin account is better than the owner's own login. Hosts: `https://api.oetwithdrhesham.co.uk`
(node) and `https://app.oetwithdrhesham.co.uk` (browser, which calls the API through `/api/backend`).

## Safety rails

- Dispatch-only, `permissions: contents: read`, concurrency group `writing-prod-qa` (no cancel-in-progress).
- `guard` refuses while `production-deploy.yml` is running or queued and validates the inputs + the 36 scripts.
- `unit` (vitest + the detector spec) must pass before `live` touches production.
- `discover` uses a read-only client: any non-GET call throws before the network (the admin sign-in POST is the
  only exception; unit-tested) and it never repairs anything.
- One admin client per run (shared sign-in, re-login once on 401). Learners are signed in by the browser only
  (single active session); the harness reads learner APIs through the signed-in page itself.
- Lane mutex: only ONE QA submission is grading at a time; per-learner pacing >= `pace_seconds`.
- Guard loop every 45 s: a deploy pauses new tests (in-flight ones become `VOID_DEPLOY`, re-run once); the
  quota marker, `failoverActive`, a non-closed Max circuit, a live-open L2/L3 circuit or any `anthropic`
  `writing.grade` usage row halts the run (in-flight tests finish, the rest are `NOT_RUN`).
- `always()` safety net: deactivates every QA fault flag it created and cleans up learners per `cleanup`
  after writing their usage/ledger evidence (the delete endpoint is a permanent purge).
- `free_samples_enabled` is read, never flipped.

## Owner rules the harness asserts (2 Oct 2026)

- **Max first:** for every graded letter the earliest `writing.grade` usage row of that learner is
  `writing-claude-sub`; otherwise the row FAILs with `max_not_first`. Only the L1+L2 fault run (flag
  `writing_grade_fault_l1l2:{userId}`, whose synthetic hops write no row) expects `writing-codex-sub` first,
  and records the flag name.
- **No quota marker:** `GET /v1/admin/ai/writing-provider` must show `quotaExceededUntil == null` at preflight
  and after the run (a non-null value is a FAIL, never repaired); a non-closed `writing-claude-sub` circuit is a
  FAIL (Max circuits are exempt); `failoverActive` is a FAIL.
- **Zero paid spend:** `paidApiSpend` = every `anthropic` `writing.grade` usage row since the run started; zero
  is expected in a normal run, anything else is a FAIL row and halts the run. Level 2 is never exercised live.

## What runs

**Discovery (every suite).** `GET /v1/professions/catalog`, `.../writing/tasks/catalogue-compatibility`,
`.../load-integrity`, flags, writing options, providers, writing-provider, circuits. A handoff profession is
**enabled** when it is present and active in the catalogue AND has >= 3 eligible tasks as the learner library
shows them (eligible = `publishReady && candidateVisible && loadOk`; the library compares
`Profession.ToLower()` with the account's `ActiveProfessionId` exactly, so a task stored under another
spelling, e.g. `occupational_therapy`, never counts and is called out). Every other handoff profession gets a
`NOT_ENABLED` row with evidence (catalogue state and task counts) - never FAIL, never dropped. Picks per
profession: LT-RR / LT-UR / LT-DG first, then (rank by rank) LT-NM/LT-OT/LT-TR fallbacks for distinct
categories, then any eligible task (noted). As of 2 Oct 2026 six professions are expected enabled (18 letters).

**Matrix.** Per enabled profession one fresh learner (`wqa-<run>-<profession>@oetwithdrhesham.co.uk`,
pre-verified, 4 credits per planned letter, 3-day expiry). Per letter: ledger C0 -> library -> click the task
-> eligibility (C1: the 2-credit task-open debit) -> reading window -> type with `pressSequentially`
(45-95 ms per key, never paste) -> server draft equals the text within 15 s -> lane -> Submit -> grading steps
read "Preparing model answer" -> graded (15 min cap; `failed` = FAIL, then Retry is exercised) -> facts
(saved letter == typed text, 6 criteria, `CandidateReady` + visible, not `deterministic-empty-v1`) -> provider
evidence -> results UI -> Post Submissions (API exactly once + UI row) -> replayed `retry-grade` is a no-op ->
credit rule.

**Acceptance (medicine).** On one attempt: S1 refresh within 5 s of the last keystroke, S2 close the browser
30 s and reopen (same storage + device), S3 offline mid-typing (`pending-local`/`offline`, then `saved`),
S5 draft PUTs answer 503 then abort, then recover, S4 shutdown (empty storage, same device, re-sign-in) and
**Resume writing** from Post Submissions; then the letter is graded. S6/S7: flag `writing_grade_fault:{id}`
(N=1) -> a real `failed` row with Retry on the grading page and in Post Submissions -> new session after
>= 2 min -> **Retry on the Post Submissions row** -> same id, one row, graded, one debit, replay no-op.
Reading-window resume (real 60 s, reload) and the L1+L2 fault letter (L3 GPT-6.1 Sol must serve). S8 on a
zero-credit learner: the free sample fails then retries; the failure burns no use, the retry counts once,
no ledger movement. The second free result is a fresh attempt ("Practice this again"); there is no revise control.

Timer verdict (away 30 s, tolerances 8 s load / +6 s autosave lag): no reset `after <= window-30`; paused
(page closed/reloaded) `before-8 <= after <= before+6`; running (page stayed mounted for `e` s)
`before-e-8 <= after <= before-e+6`. At least 45 s of the writing window are used before S1 so a reset shows.

**UI checks (each graded letter).** Desktop 1280/1366/1440/1536: every text box of `results-score-panel`
inside the card (+-1 px), no horizontal overflow of the card, its tiles or the page. Report: `result-section`
order, score `N/500`, model answer visible with `white-space: pre-wrap`, 6 criteria, corrections (more than 5
errors = 5-item preview + View all -> full list == `errors.length`; 5 or fewer = full list only), no
`undefined`/`NaN`/`[object Object]`, no appeal text or `/appeal` link, a reload keeps the grade/report ids.
Mobile 360/390/430 on a second page of the same session with the native shell emulated
(`data-runtime-kind="capacitor-native"` + MutationObserver): nothing under the handle at any scroll offset,
nothing under the bottom nav and >= 8 px clearance at the end of the page (content may scroll behind a fixed
nav mid-page), section headings / View all / next actions fully clear and top-most after `scrollIntoView`.
**Positive control:** below `lg` the handle is hidden, so emulation counts as proven only when the opened
mobile menu shows both `mobile-menu-reload-app` and `mobile-menu-check-updates` (at >= lg: the handle).
Unproven = `NOT PROVEN` (row `PARTIAL`), never PASS. The `ui` suite also checks the practice **Submit** on
Pixel 7 / iPhone 14.

**Realistic letters (`letters` suite, 5 Oct 2026).** `realistic-letters.json` holds six 200-300 word candidate letters with
planted critical/major/minor defects (from the Writing regression corpora), each pinned to ONE production scenario and typed
on a disposable learner of its profession (blank line = Enter, single newline = Shift+Enter). A letter whose scenario is not
an eligible task is `BLOCKED`, never given another task. Every report must keep: at most 3 distinct, label-free top
priorities (at most one about Purpose; none at all when every correction is advisory), criterion summaries and
per-criterion feedback <= 240 chars, no internal rule label / rule or check id / provider tag / debug term /
"Exemplar" in any report text, no value left in the neutralised candidate fields (`ruleSource`, `rulePackVersion`,
`modelVersion`, `blockingCodes`, `modelUsed`, `citedRuleIds` ...), criterion cards <= 900 visible chars with at most
one "Suggested fix:" box. The evidence keeps numbers only (`facts/*.json` -> `shape`, `cardChars`); a report without
mixed severities is `PARTIAL`.

## Test-id contract (single source: `contract.mjs`)

| Page | Ids |
| --- | --- |
| Practice | `writing-editor`, `writing-timer` (`data-phase`, `data-seconds-remaining`), `writing-draft-status` (`data-state` = saved/saving/pending-local/offline/error), `writing-submit`; editor input `div.ProseMirror#practice-editor` |
| Grading | `writing-grading-steps`, `writing-grading-failed`, `writing-grading-retry` |
| Post Submissions (`/submissions?subtest=writing`) | `post-submissions-list` (renders only with >= 1 item), `post-submission-row` (`data-submission-id` - absent for drafts, `data-scenario-id`, `data-state` = draft/grading/failed/graded), `post-submission-open/resume/retry/wait/view`, hub banner `resume-writing-banner` |
| Results | `results-score-panel`, `results-score-stat`, `grade-value`, `result-section[data-section]`, `corrections-preview`, `corrections-view-all`, `corrections-full-list`, `ai-estimated-score`, `grounded-model-answer`, `criteria-list` (6 li) |
| Shell | `shell-controls-handle` (>= lg only), `mobile-menu-reload-app`, `mobile-menu-check-updates`, `nav[aria-label="Mobile navigation"]`, `#main-content` |

The first test that reaches a page checks its group on the live DOM; a missing id makes every test that needs
the group `BLOCKED` (before it spends credits), never a false PASS.

## Statuses and evidence

`PASS | FAIL | PARTIAL | NOT_RUN | BLOCKED | VOID_DEPLOY | NOT_ENABLED`. Blocked > FAIL > PARTIAL > PASS.
PARTIAL rows list what was proven live and the run id. The verdict prints `ALL PASS (n/n tests; NOT_ENABLED:
...)` only when every test row passed; NOT_ENABLED professions are scope and listed by name.

Artifact `writing-prod-qa-evidence` (14 days): `qa2.md/.csv` (columns `Profession | Task/ID | Category |
Saved in Post Submissions | Provider used | Fallback? | Final result | Notes`), `acceptance.*`, `ui.*`,
`verdict.txt`, `run.json`, `discover.json` (plan), `preflight.json`, `tests/*.json`, `facts/*.json`
(ids, statuses, counts, usage rows), `credits/*.json`, `learners/*.json`. The same tables go to the job
summary. Artifact `writing-prod-qa-media` (3 days): element-clipped screenshots (score card, grading steps,
Post Submissions row, bottom 220 px at phone widths).

## Public-repo hygiene

The repo is public while QA runs, so artifacts are public: no letter, model-answer or case-note text in JSON
(facts are counts/ids), screenshots are clipped to elements that hold none, bearer tokens are never logged, the
per-run learner password is masked (`::add-mask::`), emails are synthetic.

## Known limits

- The detector spec and the live job are authored against contracts that land with WAI-05..09; the live run is
  unproven until those deploy.
- `fault_mode=client` proves the failure UI only; the server failure + retry path is then proven by CI.
- Content scrolling behind a fixed bottom nav mid-page is not a defect; only the end-of-page clearance and the
  probed controls are judged against the nav.
- Live L2 success is never exercised (zero API spend); a natural L2 call during QA is an incident.
