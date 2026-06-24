# BUGLOG.md — OET Prep Learner Mobile QA

Every issue found during QA is logged here with reproduction steps and severity. **All Critical/High must be fixed before internal-testing release.** Lower-severity issues are documented and triaged.

**Severity scale:**
- **Critical** — crash, data loss, security exposure, payment/auth broken, blocks a critical flow with no workaround.
- **High** — a critical flow is significantly degraded; broken on a common device/OS; no easy workaround.
- **Medium** — feature works but with notable defects; workaround exists; affects less-common config.
- **Low** — cosmetic, minor polish, rare edge case.

**Status:** Open · In Progress · Fixed · Won't Fix · Deferred

| ID | Title | Severity | Area | Device/OS | Status | Repro / Notes | Resolution |
|----|-------|----------|------|-----------|--------|---------------|------------|
| 1 | `ReadingAnswerSheetBuilder.test.tsx` flaky under full-suite load | Medium | Admin Reading authoring (test infra) | CI/local | Open (deferred) | Full `pnpm test`: 2 failures (15s timeout + "8 vs 16 calls"). Isolated re-run: 8/8 pass, exit 0, `tests 13.72s` (vs 15s global timeout). ⇒ contention flake, not a product bug. Pre-existing on `main`; not mobile. | Defer to Phase 7 CI audit. Candidate fix: raise per-test timeout for this file and/or fix cross-file mock isolation. Not a mobile release blocker. |
| 2 | `tsc` error masked by `ignoreBuildErrors` (main CI type-gate red) | High | Build/CI (billing test) | n/a | Fixed | `app/billing/page.test.tsx:77` unused `@ts-expect-error` (TS2578). `next build` ignores type errors so it slipped past; `qa-smoke` `tsc` would fail. | Removed the unused directive; `tsc --noEmit` now exits 0. Behavior unchanged. |
| 3 | Prod dep `hono <4.12.25` advisories via `@google/genai` | Low | Supply chain | n/a | Fixed | `pnpm audit --prod`: 11 advisories, all `@google/genai→@modelcontextprotocol/sdk→hono` (server-on-Lambda issues, not exercised). Does not ship in remote-URL mobile app. | `pnpm.overrides` `"hono@<4.12.25":">=4.12.25"`. Prod audit 11→6, high 1→0; `tsc` clean. Remaining 6 moderate (Sentry/OTel, server-side) tracked. |
| 4 | Deep-link verification files are placeholders | Medium | Deep links (Android App Links / iOS Universal Links) | Android/iOS | Open | `public/.well-known/assetlinks.json` → `REPLACE_WITH_YOUR_SHA256_CERT_FINGERPRINT`; `apple-app-site-association` → `TEAM_ID.com.oetprep.learner`. ⇒ `https://` App Links won't auto-verify; custom `oet-prep://` scheme still works. Served from web deploy (`public/`), so the **staging URL** must serve the real file. | Phase 8: after keystore gen, set Android SHA-256 + redeploy web; iOS TEAM ID blocked on Apple account. |

---

## Triage summary
- Critical: 0 · High: 1 (fixed) · Medium: 1 (deferred, non-mobile) · Low: 1 (in progress) — updated as QA proceeds.
