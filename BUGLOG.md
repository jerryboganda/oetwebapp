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
| 4 | Deep-link verification files are placeholders | Medium | Deep links (Android App Links / iOS Universal Links) | Android/iOS | Android Fixed; iOS blocked | `assetlinks.json` had placeholder SHA-256; `apple-app-site-association` has placeholder TEAM ID. ⇒ `https://` App Links won't auto-verify; custom `oet-prep://` scheme still works regardless. Served from web deploy (`public/`), so the **staging URL** must serve the real file. | Android: set `assetlinks.json` SHA-256 = generated keystore fingerprint `41:5F:…:7F:A9` (commit; deploy to staging to take effect). ⚠️ If distributing via Play Store (Play App Signing re-signs), also add Google's signing SHA-256 from Play Console. iOS: TEAM ID still blocked on Apple account. |

| 5 | GitHub PAT embedded in git remote URL | **Critical** | Secrets / repo credentials | n/a | Open (owner action) | `git remote -v` → `https://ghp_***(REDACTED)@github.com/jerryboganda/oetwebapp.git`. A live GitHub Personal Access Token sits in `.git/config` and is exposed to anyone with filesystem/log access (and now this session's logs). NOT in tracked history, but high blast radius (push/read per token scopes). | **1) Rotate/revoke the token now** (GitHub → Settings → Developer settings → PATs). **2) Strip it from the remote:** `git remote set-url origin https://github.com/jerryboganda/oetwebapp.git` and rely on `gh` keyring / Git Credential Manager (already `gh`-authed). I can do step 2 on request. |

---

## Triage summary
- **Critical: 1 (open — embedded PAT, owner must rotate)** · High: 1 (fixed) · Medium: 2 (1 deferred non-mobile, 1 deep-link Android-fixed/iOS-blocked) · Low: 1 (fixed) — updated as QA proceeds.
