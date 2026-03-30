# Test Coverage Map

## Coverage Legend
- `Automated strong`: covered by repeatable Playwright and/or targeted regression tests with meaningful assertions
- `Automated smoke`: route loads and key UI assertions exist, but deep mutations are not fully automated
- `Manual-style verified`: checked through focused manual/browser validation without durable full automation
- `Gap`: not sufficiently covered yet for confident regression prevention

| Surface | Coverage status | Automation status | Manual-style verification | Remaining gaps |
| --- | --- | --- | --- | --- |
| Auth entry and protected-route redirect | Automated strong | Playwright auth suite + auth bootstrap + role-guard suite | Yes | No sign-up completion flow or real email reset completion automated |
| Password recovery screens | Automated smoke | Forgot/reset navigation and client-side mismatch validation | Limited | Real email delivery/reset-code completion not automated |
| Verify-email screen | Automated smoke | Missing-email recoverability check | Limited | Full email verification completion not automated |
| Learner dashboard | Automated strong | Playwright learner smoke + accessibility smoke + page tests | Yes | No exhaustive widget/action mutation coverage |
| Learner study plan/progress/readiness/billing/settings/profile/mocks/diagnostic hubs | Automated smoke | Playwright learner route smoke | Yes | Mostly route/render coverage, not full action coverage |
| Learner submissions/history | Automated strong | Playwright smoke + page test for timestamp formatting | Yes | Compare/reopen/review action depth still limited |
| Learner reading player | Automated strong | Playwright deep-link smoke + answer/flag persistence workflow | Yes | Cross-browser deep mutation parity not automated |
| Learner listening player/results | Automated strong | Playwright immersive completion workflow in Chromium | Partial | Cross-browser and mobile completion parity not automated |
| Learner writing player/result | Automated strong | Playwright immersive completion workflow in Chromium | Partial | Cross-browser and mobile completion parity not automated |
| Learner speaking task/results | Automated strong | Playwright immersive completion workflow in Chromium | Partial | Cross-browser and mobile completion parity not automated |
| Learner immersive players overall | Automated strong on audited reading/listening/writing/speaking flows | Playwright deep-link and completion coverage | Partial | Mock-player and diagnostic immersive completion remain lighter |
| Expert dashboard/queue/calibration/metrics/schedule/learners | Automated smoke | Playwright privileged smoke | Yes | Route/render coverage only on most surfaces |
| Expert writing review workspace | Automated strong in Chromium | Playwright detail smoke + draft save + validation + final submit + rework workflow | Partial | Cross-browser deep completion parity and broader reviewer-state variants |
| Expert speaking review workspace | Automated strong in Chromium | Playwright detail smoke + tab navigation + final submit workflow | Partial | Cross-browser deep completion parity and broader reviewer-state variants |
| Expert learner detail surface | Automated smoke | Playwright detail smoke | Yes | No mutation-heavy learner-management flows automated |
| Admin operations/content/criteria/taxonomy/flags/users/billing top-level pages | Automated smoke | Playwright privileged smoke | Yes | Most surfaces remain top-level smoke only |
| Admin content create/publish/revisions | Automated strong in Chromium | Playwright deep mutation workflow | Partial | Edit/delete/archive variants and cross-browser parity not automated |
| Admin audit-log filter/detail/export | Automated strong in Chromium | Playwright deep mutation workflow + focus restore assertions | Partial | Wider export combinations and cross-browser parity not automated |
| Admin user detail mutation flows | Automated strong in Chromium | Playwright credit adjustment + reset-password + suspend/reactivate workflow | Partial | More account-state variants and billing mutations not automated |
| Role-based access control | Automated strong | Playwright role-guard suite + layout/auth hook changes | Yes | API-level negative authorization testing can expand further |
| Accessibility on high-value screens | Automated strong on audited pages | Axe smoke + keyboard/focus regression on dialogs/drawers + unit focus tests | Yes | No real screen-reader run; no exhaustive modal/dialog audit across every surface |
| Console/page/network/runtime diagnostics | Automated strong in audited suites | Diagnostics fixture attached to Playwright results | Yes | Wider surface coverage still needed beyond audited routes |
| Responsive learner surfaces | Automated smoke | Pixel 7 + iPhone 14 learner projects | Yes | Tablet layout coverage and immersive mobile completion need more depth |
| Cross-browser desktop | Automated smoke | Chromium, Firefox, WebKit smoke matrix | Yes | Deep mutation parity across browsers not yet automated |
| Locale/timezone sensitivity | Automated smoke | Sydney learner project | Limited | Wider date/time input coverage and billing/date-heavy admin flows still needed |
| Backend auth stability | Automated strong | Full .NET tests + targeted MFA regression | Yes | More end-to-end API contract checks would help |
| CI smoke workflow | Automated in repo, unobserved on GitHub | GitHub Actions workflow file exists and is runnable | No | Hosted GitHub execution still requires external observation |

## Route/Surface Inventory Notes
- Total page-route inventory discovered in `app/**/page.tsx`: broad multi-role surface including auth, learner, expert, admin, immersive players, review workspaces, and reports.
- Current Playwright suite now protects:
  - learner reading/listening/writing/speaking immersive entry and completion paths
  - expert writing and speaking review completion/rework paths
  - admin content publish/revisions, audit-log drawer/export, and user-detail mutations
- Cross-browser and mobile coverage intentionally stays smoke-level outside the highest-risk Chromium mutation paths.

## Highest-Priority Remaining Coverage Gaps
1. mock-player and diagnostic immersive completion parity
2. Firefox/WebKit deep-mutation parity for expert/admin/immersive learner flows
3. controlled email strategy for full verify-email and reset-password completion
4. human NVDA and VoiceOver signoff execution
5. GitHub-hosted observation of the repo-owned `QA Smoke` workflow
