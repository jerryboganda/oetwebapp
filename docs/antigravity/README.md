# Antigravity SDK Integration — OET with Dr. Hesham

Google Antigravity SDK runtime for **all** OET AI features, serving the
web app (Next.js), the desktop app (Tauri + local Docker stack), and the
mobile app (Capacitor, via the backend). One agent gateway, one provider row,
100% of AI features routeable through Antigravity with zero per-feature code.

## Current status (2026-08-24) — plain language

- **Students right now are served by the existing providers** (Anthropic /
  OpenAI-compatible). No feature route has been flipped to Antigravity yet.
- The gateway runs in **Mode A = a normal Gemini API key** (regular API
  quota — NOT the AI Pro subscription's Antigravity quota).
- **Mode B** = the real AI Pro Antigravity subscription quota. Only usable on
  the owner's own PC (signed-in `agy` CLI), opt-in, never for students.
- **Mode C** = official subscription quota via SDK OAuth. Google has not
  shipped it yet (watch with `pnpm ai:flipday-watch`).

### Owner operations checklist (manual, code is done)

| # | What | How |
|---|---|---|
| 1 | Quality gate before any switch | `pnpm ai:parity` (80 fixed cases vs baseline). PASS → safe to flip. |
| 2 | Switch a feature to Antigravity | Admin → `/admin/ai-providers` → open the feature route → provider `antigravity-gateway`, model `agent:<name>` → rollout **10%** → watch dashboards a day → **100%**. |
| 3 | Roll back | Same route editor → set the provider back to the old one. Instant, no deploy. |
| 4 | Desktop check | Install the desktop app on a clean PC, click through once. |
| 5 | Mobile check | Follow `docs/antigravity/mobile-validation.md` on real Android + iOS. |
| 6 | Alerting | Install `ops/prometheus/alerts-oet-gateway.yml` into the VPS monitoring rules. |

## What was built

```
agent-gateway/                 Python 3.12 service (FastAPI + google-antigravity)
  src/oet_agent_gateway/
    server.py                  /v1/chat/completions (OpenAI-compatible) + native SSE sessions
    auth.py                    Mode A gemini-key · Mode B local-oauth · Mode C sdk-oauth (flip-day)
    agents.py                  OET agent registry (8 personas)
    skills/<name>/             persona.txt + SKILL.md rulebook packages (OET grounded)
    quota.py                   daily token budgets + per-route caps + exponential backoff
    cache.py                   semantic response cache (repeated scoring prompts cost 0)
backend/.../Seeding/AntigravityGatewaySeeder.cs
                               additive provider row `antigravity-gateway` +
                               feature-route rows (idempotent, crash-safe, 5s migration grace)
docker-compose.{dev,desktop,vps,production}.yml
                               agent-gateway service (internal network, healthcheck,
                               Mode B hard-disabled in production)
.github/workflows/deploy.yml + scripts/deploy/auto-deploy-ghcr.sh
                               GHCR image build/push + blue/green rollout includes the gateway
docs/antigravity/              this doc + auth.md + roadmap.md + runbook.md +
                               parity-rubric.md + mobile-validation.md +
                               flip-day-checklist.md
scripts/antigravity/parity/    golden-set parity harness (50 writing + 30 speaking,
                               `pnpm ai:parity`) · watch_flipday.py (`pnpm ai:flipday-watch`)
ops/prometheus/                alert rules for the gateway metrics
```

## How routing works (reuses existing machinery)

1. `AntigravityGatewaySeedHostedService` seeds the provider row and route rows.
2. `AiFeatureRouteResolver` resolves a feature code (e.g. `writing.grade`) to
   `(providerCode=antigravity-gateway, model=agent:writing-examiner)`.
3. `RegistryBackedProvider` (OpenAiCompatible dialect) POSTs
   `/v1/chat/completions` to the gateway.
4. The gateway resolves `agent:<name>` → persona + rulebook skills + budget
   route, runs the Antigravity agent, returns OpenAI-shaped JSON.
5. Usage rows, grounding, and scoring parsing stay exactly where they are in
   the .NET layer (docs/AI-USAGE-POLICY.md invariants preserved).

## Feature → agent map

| Feature codes | Gateway agent |
|---|---|
| writing.grade, writing.sample_score, writing.drill.grade.v1, writing.coach.v1 | writing-examiner |
| speaking.patient.turn.v1, conversation.opening, conversation.reply, conversation.evaluation | speaking-interlocutor |
| pronunciation.tip, pronunciation.feedback | pronunciation-coach |
| admin.grammar_draft | grammar-tutor |
| admin.reading_draft, reading.explanation.v1 | reading-item-generator |
| admin.listening_draft, listening.explanation.v1 | listening-item-generator |
| card.draft.v1 (drills/cards) | drill-author |
| mock.full_grade, mock.remediation_draft | mock-analysis |

Scoring-critical routes (`speaking.score.v2` dual-grader, pronunciation
linguistic scoring with raw audio) intentionally stay on their current
providers in this phase — see roadmap.md Phase 3 rollout matrix.

## Credentials (NEVER in git)

- Gateway: `GEMINI_API_KEY` env (server-side only) + `AGENTGATEWAY_INTERNAL_SERVICE_TOKEN`.
- Backend: set `AGENTGATEWAY_INTERNAL_SERVICE_TOKEN` in the API/gateway
  environment; startup encrypts it with the existing AiProviderRegistry
  DataProtection purpose. The admin provider editor remains available for
  manual rotation.

## Validation commands

```powershell
# gateway (from repo root; uses agent-gateway/.venv)
agent-gateway\.venv\Scripts\python.exe -m pytest agent-gateway/tests -q
agent-gateway\.venv\Scripts\python.exe agent-gateway\examples\smoke_harness.py
pnpm run backend:build          # compiles the seeder + Program.cs registration
docker compose -f docker-compose.desktop.yml config --quiet
docker compose -f docker-compose.dev.yml config --quiet
```

Production route takeover is deliberately gated by
`ANTIGRAVITY_GATEWAY_ROUTES_ENABLED=false`. Set it to `true` only after both
the gateway Gemini key and the encrypted `antigravity-gateway` backend token
are configured and a smoke request has passed.
