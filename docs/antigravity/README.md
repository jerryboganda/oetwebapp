# Antigravity SDK Integration — OET with Dr. Hesham

Google Antigravity SDK runtime for **all** OET AI features, serving the
web app (Next.js), the desktop app (Tauri + local Docker stack), and the
mobile app (Capacitor, via the backend). One agent gateway, one provider row,
100% of AI features routeable through Antigravity with zero per-feature code.

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
docs/antigravity/              this doc + auth.md + roadmap.md + runbook.md
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
