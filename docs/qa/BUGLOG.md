# OET Prep Desktop (Tauri 2) — Bug & Issue Log

Severity: **Critical** (blocks release) · **High** (fix before release) · **Med** (fix or document) ·
**Low** (nice-to-have). Status: OPEN / FIXED / DEFERRED / WONTFIX.

| ID | Sev | Status | Title | Found in | Notes / resolution |
|----|-----|--------|-------|----------|--------------------|
| BUG-001 | Low | **FIXED** | 4 clippy warnings in `src/lib.rs` (`unnecessary_mut_passed`, `needless_borrows_for_generic_args` ×3) | Phase 2 | Fixed on branch; `cargo clippy -- -D warnings` now exit 0. |
| BUG-002 | Med | **OPEN/DEFERRED** | Deep link `oet-prep://pair?code=…` navigates to `/pair`, which has no route → 404 | Phase 0/3 | `lib.rs:95` maps it; no `app/pair/` page exists. **Frontend scope (out of this branch).** Tray/`/dashboard`/`/study-plan` routes are fine. Flagged for the frontend team. |
| BUG-003 | High | **OPEN** | Sidecar stdout/stderr discarded (`Stdio::null()`), so production startup failures are undiagnosable | Phase 0/2 | `sidecar.rs:152-153,177-178`. Fix in Phase 6: capture to a rotated log under app-data. |
| BUG-004 | Critical | **CONFIG FIXED** | Auto-updater shipped pointing at `http://127.0.0.1:8765/latest.json` with a throwaway minisign key | Phase 0 | Phase 3: generated fresh prod minisign key (private key outside repo, → CI secret); pubkey + HTTPS endpoint (`app.oetwithdrhesham.co.uk/desktop/updates/latest.json`) wired in `tauri.conf.json`. **Remaining:** host feed on prod server + set CI secret (Phase 7/8). |
| BUG-005 | High | **OPEN** | No Windows Authenticode signing → SmartScreen "unknown publisher" | Phase 0 | Fix in Phase 8: self-signed cert + tester trust steps; Azure Trusted Signing documented as upgrade. |
| BUG-006 | Med | **ACCEPTED** | Broad IPC ACL — all 17 commands granted to any `http://localhost*` / `127.0.0.1*` origin | Phase 0 | Phase 3 audit: ACL is **loopback-only** (no external origins); port wildcard required by dynamic renderer port; all grants are contract commands with input validation. Webview only ever navigates to the spawned renderer. Residual risk accepted + documented in QA_REPORT §Phase 3. |

## Notes
- BUG-002 is intentionally **out of scope** for this `src-tauri`-only branch (it's a Next.js route).
  Logged here so it isn't lost; recommend the frontend team add `app/pair/page.tsx` (or redirect)
  before relying on device-pairing deep links in the desktop build.
- New issues discovered during Phases 4–9 will be appended with repro steps.
