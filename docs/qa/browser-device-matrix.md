# Browser and Device Matrix

## Tested Matrix

| Project | Browser / Device | Auth state | Coverage focus | Latest status | Notes |
| --- | --- | --- | --- | --- | --- |
| `chromium-unauth` | Desktop Chrome | Unauthenticated | auth, redirects, sign-in accessibility | Passed | Used for auth entry and recovery smoke |
| `chromium-learner` | Desktop Chrome | Learner | learner smoke, accessibility, role guards, deep links, immersive completion | Passed | Includes reading-player state retention plus listening/writing/speaking completion workflows |
| `chromium-expert` | Desktop Chrome | Expert | expert smoke, accessibility, detail routes, deep review completion | Passed | Includes MFA branch, writing/speaking final submit, shortcut save, and rework validation |
| `chromium-admin` | Desktop Chrome | Admin | admin smoke, accessibility, detail routes, deep CRUD/export/user mutations | Passed | Includes content publish/revisions, audit-log drawer/export, and user credit/password/account actions |
| `firefox-learner` | Desktop Firefox | Learner | learner smoke, selected deep links | Passed | Cross-browser learner sanity plus reading player and mock report smoke |
| `firefox-expert` | Desktop Firefox | Expert | expert smoke, selected detail routes | Passed | Review/detail smoke only; deep completion remains Chromium-only |
| `firefox-admin` | Desktop Firefox | Admin | admin smoke, selected detail routes | Passed | Content/user detail smoke only; deep mutations remain Chromium-only |
| `webkit-learner` | Desktop Safari/WebKit | Learner | learner smoke, selected deep links | Passed | Verified after runtime asset fix; includes reading player and mock report smoke |
| `webkit-expert` | Desktop Safari/WebKit | Expert | expert smoke, selected detail routes | Passed | Review/detail smoke only; deep completion remains Chromium-only |
| `webkit-admin` | Desktop Safari/WebKit | Admin | admin smoke, selected detail routes | Passed | Content/user detail smoke only; deep mutations remain Chromium-only |
| `mobile-chromium-learner` | Pixel 7 | Learner | learner smoke, responsive learner layout | Passed | Mobile learner baseline |
| `mobile-webkit-learner` | iPhone 14 | Learner | learner smoke, responsive learner layout | Passed | Mobile Safari-style learner baseline |
| `sydney-learner` | Desktop Chrome, `Australia/Sydney` | Learner | learner date/time sanity | Passed | Locale/timezone smoke |

## Issues Found Per Environment
- Chromium:
  - surfaced the initial accessibility defects that were remediated
  - surfaced the admin modal focus-restore defect and the admin audit-log drawer focus defect, both now resolved
  - surfaced the seeded listening media defect in the immersive learner stack, now resolved
  - surfaced the expert keyboard-shortcut reliability issue, now resolved
- Firefox:
  - helped reproduce privileged auth-state stability issues before MFA fix
  - previously emitted harmless `NS_BINDING_ABORTED` noise that is now filtered in diagnostics
- WebKit:
  - helped surface runtime sensitivity to missing public assets and privileged auth-state branch issues
- Mobile:
  - exposed the missing accessible-name issue on the compact top-nav home link

## Unsupported or Untested Cases
- No legacy-browser support testing was performed.
- No branded-browser-specific pass was run beyond Playwright engine coverage.
- No Android tablet or iPad tablet-specific matrix project was added in this pass.
- No forced-colors, dark-mode, or real screen-reader matrix was executed.
- Deep mutation parity in Firefox/WebKit/mobile remains intentionally unexpanded beyond smoke coverage.
- The repo-owned `QA Smoke` GitHub Actions workflow was added, but its GitHub-hosted execution was not observed during this local audit.

## Matrix Interpretation
- The tested matrix is strong enough for release-risk smoke confidence.
- Chromium now carries the deeper mutation coverage for the highest-risk learner, expert, and admin workflows.
- This is not a claim of identical correctness across every browser/device workflow.
- Highest-value next additions:
  1. tablet learner project
  2. Firefox/WebKit parity for expert completion flows
  3. Firefox/WebKit parity for admin deep CRUD/export flows
  4. reduced-motion / zoom / dialog-focused accessibility matrix
