# Flip-Day Checklist — Mode C (`sdk-oauth`) Activation

Google has not shipped first-class SDK OAuth yet
(google-antigravity/antigravity-sdk-python **issue #20**). When it lands, this
checklist turns the gateway onto real AI Pro **Antigravity** quota with a
config change — no app code changes.

## 0. Watch (ongoing)

```powershell
agent-gateway\.venv\Scripts\python.exe scripts\antigravity\watch_flipday.py
```

Prints: latest `google-antigravity` version on PyPI vs the pin in
`agent-gateway/pyproject.toml`, and issue #20 state. Run monthly; also watch
antigravity.google/changelog.

## 1. Confirm release

- [ ] Issue #20 closed as completed with an SDK release note
- [ ] New SDK version documents OAuth credentials for `LocalAgentConfig`
- [ ] Changelog shows no breaking `Agent` / chunk-type API changes

## 2. Implement the adapter

- [ ] Replace the stub body of `SdkOAuthAuth.apply` in
      `agent-gateway/src/oet_agent_gateway/auth.py` with the official client
      delegation (keep the class + `mode` name stable)
- [ ] Bump the `google-antigravity` pin if required; run contract tests
- [ ] `pytest agent-gateway/tests` green

## 3. Validate

- [ ] `examples/smoke_harness.py` with OAuth credentials → real turn
- [ ] Parity harness PASS on both suites:
      `scripts\antigravity\parity\run_parity.py ... --report flipday-parity.md`
- [ ] Soak: 100-turn local run, no session-pool growth leaks

## 4. Roll out

- [ ] `.env.production` (+ staging/dev/desktop envs): `AGENTGATEWAY_AUTH_MODE=sdk-oauth`
- [ ] Remove `GEMINI_API_KEY` from VPS env after cutover confirmed
- [ ] Deploy via blue/green; verify `/v1/healthz` shows `auth_mode=sdk-oauth`,
      `auth_ready=true`
- [ ] Promote routes at 10% per `docs/speaking/ai-providers.md`, watching the
      `/v1/metrics` alert rules (`ops/prometheus/alerts-oet-gateway.yml`)
- [ ] Archive Mode B: set `AGENTGATEWAY_LOCAL_OAUTH_ALLOWED=false` everywhere;
      mark `LocalOAuthAuth` deprecated in `docs/antigravity/auth.md`

## 5. Post-flip

- [ ] Update roadmap.md (flip-day done) and this file's status header
- [ ] Keep the Gemini-key adapter intact as disaster-recovery fallback
