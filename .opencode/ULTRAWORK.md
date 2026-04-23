# OET Prep Platform — ULTRAWORK mode is ACTIVE

This project is configured to run `oh-my-opencode` (omo) at full power. You
just type requirements; Sisyphus does the rest.

---

## Launch (one time per terminal)

### PowerShell (recommended on Windows)

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
. .\.opencode\activate.ps1
opencode
```

### cmd.exe

```cmd
.opencode\activate.cmd
opencode
```

That script primes `PATH` (so `ast-grep` + `comment-checker` are resolvable)
and disables omo telemetry.

---

## How to use it

You don't need to remember commands. Just type what you want:

| You type … | What happens under the hood |
| --- | --- |
| "`add server-side CSV export to /admin/users`" | `keyword-detector` → `auto-slash-command` invokes Prometheus → interviews you just enough → produces Decision-Complete plan → Sisyphus delegates parallel subtasks to Hephaestus (backend), visual-engineering (frontend), Momus (review) → Ralph-loop drives to 100% → Git-master commits atomically |
| "`ultrawork` or `ulw` + <goal>" | Explicit full-power trigger. Same chain, most aggressive mode. |
| "`fix this bug: <paste>`" | `think-mode` + `todo-continuation-enforcer` + direct Oracle + Hephaestus. |
| "`refactor the writing editor`" | `/refactor` command, Sisyphus coordinates LSP renames + AST-Grep rewrites. |

Press **Tab** at any prompt to cycle into Prometheus (plan) mode manually.

Say **`/start-work`** to execute the current plan.

---

## Agent roster (all active, all routed to DigitalOcean Inference)

| Agent | Role | DO model |
|---|---|---|
| **Sisyphus** | Main orchestrator | `do-claude-opus-4.7` (max) |
| **Prometheus** | Planner (interview mode) | `do-claude-opus-4.7` (max) |
| **Metis** | Plan reviewer | `do-claude-opus-4.7` (max) |
| **Hephaestus** | Deep autonomous worker | `openai-gpt-5.4` (high) |
| **Oracle** | Architecture / debugging | `openai-gpt-5.4-pro` (high) |
| **Momus** | Verification / strict review | `openai-gpt-5.4-pro` (xhigh) |
| **Atlas** | Todo orchestrator | `do-claude-sonnet-4.6` |
| **Librarian** | Docs & code search | `openai-gpt-5-mini` |
| **Explore** | Fast grep | `openai-gpt-5-mini` |
| **Multimodal-Looker** | Vision / screenshots | `openai-gpt-5.4` |
| **Sisyphus-Junior** | Background chores | `openai-gpt-5-mini` |

Every agent has DO-only fallback chains so a transient 429/503 on one model
automatically retries on the next in line — no stalls.

## Task categories (for `task()` delegation)

`quick`, `unspecified-low`, `unspecified-high`, `deep`, `ultrabrain`,
`visual-engineering`, `writing`, `artistry` — all DO-routed.

## Active hooks (productivity stack)

- `keyword-detector` — catches intent from free-text
- `auto-slash-command` — routes to the right command without explicit `/`
- `start-work` — auto-plans via Prometheus on complex asks
- `ralph-loop` — self-referential "don't stop until done"
- `ulw-loop` — ultrawork loop
- `todo-continuation-enforcer` — yanks Sisyphus back if it goes idle
- `think-mode` — deep reasoning for hard problems
- `session-recovery` — heals thinking-block / context-window / API errors
- `context-window-monitor` + `preemptive-compaction` — pre-emptive context saves
- `dynamic_context_pruning` — auto-prune old tool outputs
- `comment-checker` — strips AI-slop comments
- `compaction-context-injector` — re-injects AGENTS.md after compaction
- `anthropic-context-window-limit-recovery` — recover on 200k+ contexts
- `runtime-fallback` — auto-switch models on 400/408/425/429/500/502/503/504/529
- `thinking-block-validator` — keeps Anthropic thinking blocks valid
- `non-interactive-env` + `interactive-bash-session` — shell pairing
- `auto-update-checker` + `startup-toast` — keeps omo current

## Tools available to every agent

- **Hashline Edit** (replaces Edit) — content-hash anchored; stale-line
  edits are mathematically impossible
- **LSP**: `lsp_rename`, `lsp_goto_definition`, `lsp_find_references`,
  `lsp_diagnostics` via `typescript-language-server`
- **AST-Grep** (bundled) — structural code search & rewrite for 25
  languages; see `.agents/skills/ast-grep/SKILL.md` for OET-specific recipes
- **Context7 MCP** — real Next.js 15 / React 19 / Tailwind 4 / EF Core /
  ASP.NET Core 10 / Capacitor docs on demand
- **grep.app MCP** — GitHub-wide code search
- **All 77 `.agents/skills/*`** — your existing skill catalog (Capacitor,
  Next.js best practices, motion-system, OET-specific GSD agents, etc.)
- **All 24 GSD agents** in `.opencode/agents/gsd-*.md` — Sisyphus can
  delegate to them by name when a task maps to an OET domain
  (`gsd-planner`, `gsd-executor`, `gsd-verifier`, `gsd-roadmapper`,
  `gsd-security-auditor`, `gsd-code-reviewer`, `gsd-code-fixer`,
  `gsd-doc-verifier`, `gsd-nyquist-auditor`, etc.)
- **Sisyphus task system** — cross-session task tracking in
  `.sisyphus/tasks/` (add `.sisyphus/` to .gitignore if not already)

## OET invariants Prometheus/Sisyphus will respect

The `prometheus` agent's `prompt_append` explicitly points at `AGENTS.md`
for these mission-critical rules (Sisyphus reads the same file via the
built-in AGENTS.md injector):

1. **OET Scoring** — all routes through `lib/scoring.ts` / `OetScoring`
2. **Rulebooks** — all access via `lib/rulebook` / `Services.Rulebook`
3. **AI Gateway** — every AI call through `buildAiGroundedPrompt()` / `AiGatewayService`
4. **Content Uploads** — `ContentPaper` → `ContentPaperAsset` → `MediaAsset`, all via `IFileStorage`
5. **OET Statement of Results** — `OetStatementOfResultsCard` is pixel-faithful — never restyle
6. **Reading Authoring** — 20 + 6 + 16 = 42 items, grading via `ReadingGradingService`
7. **Grammar Module** — AI drafts via `GrammarDraftService`, rulebook-grounded
8. **Pronunciation** — server-authoritative, ASR provider via `IPronunciationAsrProviderSelector`, 70/100 ≡ 350/500
9. **AI Conversation** — mean 4.2/6 ≡ 350/500, grounded prompt required

## Build gate (runs automatically on Ralph-loop close)

`npx tsc --noEmit` · `npm run lint` · `npm test` · `npm run build` ·
`npm run backend:build` · `dotnet test backend/OetLearner.sln`

## What to avoid

- **Don't disable `no-sisyphus-gpt` hook** — it prevents Sisyphus from
  being routed to a pure GPT model (which has no matching prompt path).
- **Don't re-enable `init-deep`** — it would auto-rewrite AGENTS.md which
  encodes mission-critical OET invariants.
- **Don't delete `.sisyphus/`** — that's your cross-session task state.

## Rollback (if anything misbehaves)

```cmd
git checkout .opencode/opencode.json .opencode/package.json
del .opencode\oh-my-opencode.jsonc .opencode\ULTRAWORK.md .opencode\activate.cmd .opencode\activate.ps1
rmdir /s /q .opencode\node_modules .sisyphus
cd .opencode && npm install
```

Then remove `"plugin": ["oh-my-openagent"]` from
`%USERPROFILE%\.config\opencode\opencode.json`. You're back to the
previous setup in under a minute.
