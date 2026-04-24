# Sprint Numbering Reconciliation

**Date:** 2026-04-24
**Purpose:** Resolve the `H1–H5 / M1–M8 / L1–L3` numbering collision between the original Sprint‑2 roadmap and the auth‑security audit closed in commit `7d68a7b`. Both schemes use the same `H#/M#/L#` prefix but index different workstreams, which has already produced at least one internal confusion event. This doc is the permanent cross‑walk.

**Scope:** Documentation only. No source changes. No commits to app code. No policy changes.

---

## 1. The Two Schemes

### Scheme A — Sprint‑2 "Hardening" roadmap (original)

The engineering sprint roadmap that drove commits from `9fa14a7` (Sprint‑1 close) through `7d68a7b`. Uses `H1`…`H15` for individual hardening items across auth, observability, resilience, pairing, desktop packaging, and E2E.

Commits that explicitly cite this scheme:

| Sprint‑2 code | Commit | Subject |
| --- | --- | --- |
| H2 | `f68fd16` | auth rate limiting + rulebook grounding hardening (together with DESIGN token sweep) |
| H9 | `9f965ef` | ai‑gateway grounding‑refusal coverage for LetterType / CardType |
| H10 (.NET) | `2a2a24e` | Sentry backend wiring with PII scrub |
| H10 (Next) | `1b1c50c` | Sentry Next.js wiring with PII scrub |
| H11 | `ae5c39e` | external‑auth token via URL fragment + harness DI repair |
| H12 | `c8b2369` | Windows Electron release workflow |
| H13 | `15526f6` | device pairing code broker + deep‑link dispatch |
| H15 | `5c7562d` | BrevoEmailSender Polly resilience (retry + circuit‑breaker + timeout) |

Items **H1, H3, H4, H5, H6, H7, H8, H14** remained tracked under this scheme when commit `7d68a7b` landed.

### Scheme B — Auth Security Audit (backend authentication audit)

A separate 13‑item audit of the authentication subsystem that was closed in a **single squash commit** `7d68a7b feat(security): close auth security audit (H1-H5, M1/M2/M4/M6-M8, L1/L3) + harden production config`. Uses its own `H1`…`H5`, `M1`…`M8`, `L1`…`L3` indexing scoped **only to the auth audit**, not the broader Sprint‑2 work.

Mapping is fully recoverable from the commit body:

| Audit code | Finding (auth audit scope) | Implementation anchor |
| --- | --- | --- |
| **H1** (audit) | Per‑account sign‑in lockout | `ApplicationUserAccount.FailedSignInCount` + `LockoutUntil`; exponential backoff after 5 failures in `AuthService.SignInAsync` |
| **H2** (audit) | OTP / TOTP / recovery‑code attempt caps | Memory‑cached counters |
| **H3** (audit) | Refresh‑token family reuse detection | `RefreshTokenRecord.FamilyId` carried through every rotation; `RevokeRefreshTokenFamilyAsync` |
| **H4** (audit) | Honor provider `email_verified` before linking external identities | `ExternalAuthService` / `ExternalIdentityProviderClient` |
| **H5** (audit) | Refresh token via HttpOnly SameSite=Strict cookie for web | body token gated by `X-OET-Client-Platform: capacitor\|desktop\|native` |
| **M1** (audit) | Enumeration‑safe email‑verify OTP | `EmailOtpService` |
| **M2** (audit) | Timing‑safe sign‑in (dummy PBKDF2 on unknown email) | `AuthService.SignInAsync` |
| **M4** (audit) | Sign‑out rate limit | endpoint policy |
| **M6** + **L3** (audit) | Constant‑time hash and TOTP comparisons | `AuthenticatorTotp` + hash helpers |
| **M7** (audit) | 80‑bit recovery codes | `EmailOtpService` recovery path |
| **M8** (audit) | HTML‑encode email body | `HtmlSanitizerService` + `HtmlSanitizerServiceTests` |
| **L1** (audit) | JWT `ClockSkew` tightened from default 5m to 60s | `Program.cs` |

**M3**, **M5**, **L2** from the audit were intentionally deferred; they are not in `7d68a7b`.

---

## 2. Why the Collision Matters

- Both schemes share the `H1–H5 / M1 / M4` codes. A raw search for `H1` in the git log returns commits from **both** schemes and two different engineering contexts.
- Agent handoffs that say "H3 FE" or "original H1" rely on context the agent must already have. Without this doc, a cold reader can't disambiguate.
- Follow‑on test‑and‑verify tasks (`T-c2dced37` H3 FE, `T-b1acc1e4` original H1, `T-95d02d06` H4, `T-2edbbc84` H8 E2E) all belong to **Scheme A (Sprint‑2)**, not the audit scheme.

---

## 3. Canonical Disambiguation Rules

From now on, any H/M/L reference in commits, PRs, or plans **must** use one of these two forms:

1. **Sprint‑2 roadmap** items: write `Sprint-2/H3`, `Sprint-2/H8`, etc.
2. **Auth audit** items: write `auth-audit/H1`, `auth-audit/M7`, etc.

Existing commits are frozen, but the disambiguation lives here.

---

## 4. Sprint‑2 Status (authoritative)

| Sprint‑2 code | Title | Status | Commit / Task |
| --- | --- | --- | --- |
| H1 | (original item — see open task) | ⏳ Pending | `T-b1acc1e4` |
| H2 | Auth rate limiting + rulebook grounding | ✅ Landed | `f68fd16` |
| H3 | Admin audit‑log FE scaffold | ⏳ Pending | `T-c2dced37` against `AdminEndpoints.cs:181,186,190` |
| H4 | (original item — see open task) | ⏳ Pending | `T-95d02d06` |
| H5 | (original item) | ⏳ Pending | No task created yet |
| H6 | (original item) | ⏳ Pending | No task created yet |
| H7 | (original item) | ⏳ Pending | No task created yet |
| H8 | E2E coverage | ⏳ Pending | `T-2edbbc84` |
| H9 | AI‑gateway grounding‑refusal tests | ✅ Landed | `9f965ef` |
| H10 | Sentry wiring (Next + .NET, PII scrub) | ✅ Landed | `1b1c50c` + `2a2a24e` |
| H11 | External auth token via URL fragment | ✅ Landed | `ae5c39e` |
| H12 | Windows Electron release workflow | ✅ Landed | `c8b2369` |
| H13 | Device pairing code broker + deep‑link | ✅ Landed | `15526f6` |
| H14 | (original item) | ⏳ Pending | No task created yet |
| H15 | BrevoEmailSender Polly resilience | ✅ Landed | `5c7562d` |

**Sprint‑2 landed:** 8 of 15 (H2, H9, H10, H11, H12, H13, H15 + the shared grounding work in `9f24cb0`).
**Sprint‑2 pending:** 7 (H1, H3, H4, H5, H6, H7, H8, H14 — some still without a created task).

> Titles marked "(original item)" need a product spec backfill before they can be scheduled. This doc does not fabricate them.

---

## 5. Auth‑Audit Status (authoritative)

All items listed in commit `7d68a7b` are landed and verified in‑tree:

- Tests green: `AuthFlowsTests` 57/57; new `HtmlSanitizerServiceTests`, `MediaEndpointSecurityTests`, `PaymentGatewaySecurityTests` all passing at commit time.
- Migration `AddAuthLockoutAndRefreshFamily` landed with reversible `Down` and designer file repair (`040f43f`).
- Production config hardening shipped in the same commit: `Billing:AllowSandboxFallbacks` must be `false` outside Development; Stripe required keys enforced; `ApplicationUserRoles` constants replace string‑literal policies; `RulebookReader` + `AiCaller` + `Sponsor` policies added.
- Frontend contract updates landed alongside: `lib/auth-storage.ts`, `lib/auth-client.ts`, `lib/api.ts`, `lib/backend-proxy.ts` (the `X-OET-Client-Platform` header dance).

**Auth audit deferred (not in `7d68a7b`):** M3, M5, L2 — each needs its own follow‑up commit when scheduled.

---

## 6. Follow‑on Tasks (Sprint‑3, Sprint‑4)

Only the items already mentioned in the project handoff are recorded here — no speculation:

- **Sprint‑3** — H5 / H6 / H7 + M3 OTel (observability expansion). Gap check pending.
- **Sprint‑4** — M1 / M2 / M6 / M8 / M9 (policy + enforcement). Gap check pending.
- **C1 phase‑2** — follow‑up to Sprint‑1 C1–C7 (`9fa14a7`). Scope not yet written.
- **Staging checklist** — blocked on staging environment availability.

These are placeholders until product writes the specs; nothing is committed against them.

---

## 7. References

- `7d68a7b` — auth audit squash commit (Scheme B closure).
- `9fa14a7` — Sprint‑1 C1–C7 closure.
- `f68fd16`, `9f965ef`, `1b1c50c`, `2a2a24e`, `ae5c39e`, `c8b2369`, `15526f6`, `5c7562d`, `9f24cb0` — Sprint‑2 Scheme A landings.
- `040f43f` — EF Designer.cs repair for the auth‑audit migration.
- `docs/TECH-DEBT-CLEANUP-PLAN.md`, `docs/UNUSED-CODE-AUDIT.md` — related hygiene docs.

---

## 8. Change Log

- **2026-04-24** — Initial reconciliation written against tree at `40feb09`. Sources: `git log --format='%H %s'`, full body of `7d68a7b`, and the existing handoff summary.
- **2026-04-25** — Hygiene-only update (no Sprint-2 items moved). Related commits since `40feb09`: `a584841` (Unit 7 — `scripts/ts-prune-filter.mjs` + `npm run unused:scan`), `8f8986f` (Unit 2 — ts-prune installed), `ca9b0a8` (Unit 8 step 2 — `capacitor-voice-recorder@6.0.3` installed, `@ts-expect-error` removed at `lib/mobile/pronunciation-recorder.ts:44`). `docs/ESLINT-SUPPRESSIONS-INVENTORY.md`, `docs/TS-PRUNE-TRIAGE.md`, and `docs/UNUSED-CODE-AUDIT.md` updated to match. Sprint-2 pending count unchanged (H1, H3, H4, H5, H6, H7, H8, H14).
