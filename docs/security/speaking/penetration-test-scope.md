# Speaking Module — Penetration Test Scope

## In scope

### Endpoints
- All `/v1/speaking/*` (learner)
- All `/v1/expert/speaking/*` and `/v1/expert/training/*`
- All `/v1/admin/speaking/*`
- All Speaking SignalR hubs (`/v1/conversation/hub`, `/v1/speaking/live-rooms/hub`)
- LiveKit webhook receiver `/v1/speaking/live-rooms/webhooks/livekit`

### Flows
- Sign-in → role-play card list (profession filter)
- AI self-practice flow (warm-up → prep → role-play → assessment)
- Two-role-play mock flow
- Live tutor flow (LiveKit token mint, room join, tutor cue, egress, webhook)
- Tutor assess + calibration
- Drill attempt + scoring
- Recording self-management (list, delete)
- Admin card AI-draft + batch generation
- Admin recording-access audit

### Data
- `InterlocutorScript` exposure to learner (must not leak)
- Cross-tenant access (learner A trying to read learner B's session)
- Profession IDOR (Nursing learner trying to read Pharmacy card)
- Recording playback URL bound to caller
- Audit log immutability

### Infra
- S3 bucket policy (anonymous, listing, public ACL)
- LiveKit webhook signature bypass
- Provider key handling + log redaction

## Out of scope

- Anthropic / OpenAI / ElevenLabs internal infrastructure.
- LiveKit Cloud internal infrastructure.
- Generic web framework CVEs (Next.js, ASP.NET Core) — defer to upstream advisories.
- Browser extensions and end-user devices.
- Mobile + desktop wrappers (separate engagement).

## Rules of engagement

- **Test data only**: use a dedicated test tenant. Do not test against production learner data.
- **Authenticated testing**: pre-provisioned learner, tutor, admin accounts will be supplied.
- **Rate limits**: do not bypass rate limits to flood production. Coordinate aggressive scenarios with ops.
- **AI providers**: do not test prompt-injection abuse against production billing — request a dev key with low spend cap.
- **DoS**: load testing scenarios coordinated with `tests/load/` budgets.

## Expected finding categories

Roughly in order of priority:

1. **Auth bypass** — JWT validation, refresh-token reuse, MFA bypass.
2. **IDOR** — session, recording, card, assessment ownership.
3. **Prompt injection** — card content, transcript text.
4. **File upload** — recording chunks, audio MIME confusion, oversized chunks.
5. **Webhook spoofing** — LiveKit signature, replay attacks.
6. **Information disclosure** — `InterlocutorScript` leakage, transcript cross-read, error-message leak.
7. **Server-side request forgery** — provider URLs, S3 pre-signed URLs.

## Reporting

- Findings go into the standard CVSS template.
- High / Critical: 24h disclosure window.
- Medium: 7 days.
- Low / Informational: ship with next release notes.

## Re-test policy

- Every Critical / High requires a re-test before the engagement closes.
- Re-test budgeted in engagement scope.
