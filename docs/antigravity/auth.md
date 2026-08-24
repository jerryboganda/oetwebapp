# Antigravity Gateway — Auth Modes & Quota Strategy

The Google Antigravity SDK (`google-antigravity` v0.1.14, PyPI 2026-08-22)
does **not** yet support consumer OAuth (Google account / AI Pro subscription).
Official credential paths today: `GEMINI_API_KEY` or Vertex/Agent Platform
(ADC). SDK OAuth is an open request (google-antigravity/antigravity-sdk-python
issue #20, Google: "actively investigating"). This file documents the three
adapter modes shipped by the gateway.

## Mode A — `gemini-key` (default, production, official)

- `AGENTGATEWAY_AUTH_MODE=gemini-key` + `GEMINI_API_KEY` server-side.
- This is the owner's Google AI Pro account Gemini API key (raised-tier limits).
- Hardening applied by the gateway:
  - key never leaves the container (no client bundles);
  - AI Studio restriction: restrict the key to the VPS egress IP (unrestricted
    keys are rejected by Gemini API since 2026-06-19 anyway);
  - daily token budget + per-route caps (`AGENTGATEWAY_BUDGET_*`);
  - semantic cache (TTL 1h, 512 entries) — repeated scoring prompts cost 0;
  - exponential backoff + jitter on 429/RESOURCE_EXHAUSTED, then the .NET
    `AiFeatureRouteResolver` fallback chain takes over (Anthropic/OpenAI).

## Mode B — `local-oauth` (OPT-IN, personal use only — owner risk call)

- Reuses the signed-in `agy` CLI OAuth session (Windows Credential Manager /
  macOS Keychain; service `gemini`, account `antigravity`, overridable) and
  routes model calls through the Cloud Code consumer endpoint
  (`daily-cloudcode-pa.googleapis.com/v1internal` + Bearer token).
- **This consumes the owner's Google AI Pro Antigravity quota directly.**
- ⚠️ RISK NOTICE (owner accepted 2026-08-24): Google Antigravity ToS §6
  prohibits third-party applications from bridging consumer OAuth sessions;
  community reports document account suspensions for this pattern. The owner
  operates multiple AI Pro accounts and explicitly accepted the risk.
- Gates:
  - `AGENTGATEWAY_LOCAL_OAUTH_ALLOWED=true` required (default false);
  - hard-disabled in `docker-compose.production.yml` and `vps.yml`;
  - enabled only in desktop/dev compose via the owner's own `.env`;
  - never placed in front of multi-user learner traffic.

## Mode C — `sdk-oauth` (flip-day stub)

- Raises a descriptive error today; becomes the production path the moment
  Google ships first-class SDK OAuth. Flip procedure in roadmap.md.

## Quota reality (multi-user SaaS)

`ANTIGRAVITY_GATEWAY_ROUTES_ENABLED` is an explicit route-takeover gate. The
production default is `false`; the startup seeder creates an inactive provider
placeholder but does not redirect learner features until this flag is enabled
and the backend provider token exists. The token is encrypted automatically
from `AGENTGATEWAY_INTERNAL_SERVICE_TOKEN`.

One personal AI Pro subscription cannot serve all paying students at peak:
- quota refreshes every ~5 hours with a weekly cap;
- plan = tiered usage: reserve Antigravity for high-value scoring routes,
  keep `AiFeatureRouteResolver` fallbacks for overflow;
- the gateway governor makes exhaustion visible (`/v1/quota`,
  `/v1/healthz`) instead of failing learner turns.
