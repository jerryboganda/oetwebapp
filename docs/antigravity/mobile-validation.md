# Mobile Validation — Antigravity Gateway (Phase 5)

The mobile clients (Capacitor Android/iOS) need **zero client changes**: all
AI features flow through the .NET backend → `oet-agent-gateway`. This phase
verifies the staged backend behaves identically for mobile transports.

## Preconditions

1. Staging (or local compose) has the gateway running and at least one route
   pointed at `antigravity-gateway` via `/admin/ai-providers` (e.g. a low-risk
   route like `pronunciation.tip`).
2. Capacitor app builds against the staging API base URL.
3. SignalR hub reachable from the device (used by speaking session progress).

## Verification matrix

| # | Check | Android | iOS | Notes |
|---|---|---|---|---|
| 1 | Writing assessment submit → score returned, spinner ≤ baseline latency | ☐ | ☐ | Route through gateway; usage row recorded |
| 2 | Speaking role-play: 6 consecutive patient turns, no dropped stream | ☐ | ☐ | Native SSE consumed server-side → SignalR to client |
| 3 | Pronunciation tip after ASR transcript | ☐ | ☐ | Exercises pronunciation-coach agent |
| 4 | Background/foreground mid-turn: turn completes or fails gracefully with retry CTA | ☐ | ☐ | No stuck spinners |
| 5 | Airplane-mode toggle during turn: error surfaced ≤ timeout window | ☐ | ☐ | Backend 504/503 maps to friendly copy |
| 6 | Weak-network (throttled 3G) full writing grade | ☐ | ☐ | Verify keepalive prevents proxy cuts on SSE paths |
| 7 | Quota-exhaustion drill: set tiny `AGENTGATEWAY_BUDGET_TOKENS_PER_DAY`, confirm fallback provider answers transparently | ☐ | ☐ | Student never sees an error; answer quality consistent |
| 8 | Circuit-open drill: stop gateway container, confirm fallback engages instantly | ☐ | ☐ | Validates fast-fail handoff end-to-end |
| 9 | Token-leak scan: no gateway token/key in app bundle, logs, or devtools | ☐ | ☐ | `grep` the built assets for the internal token |

## Evidence

Record device model + OS version per checkbox; attach screenshots of checks
7–8 (the resilience drills) to the release notes. Sign-off: owner.

## Rollback

Mobile rollback = flip the route back to the incumbent provider in
`/admin/ai-providers` (no app release required — `server.url` is remote-only,
so fixes ship server-side instantly).
