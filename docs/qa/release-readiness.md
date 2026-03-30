# Release Readiness

## Current Status
- Recommendation: `Conditional Go`
- Decision basis:
  - no known critical defects remain
  - no known high-severity defects remain
  - no known reproduced medium defects remain from the audited surface
  - Playwright smoke matrix and backend tests are green
  - Chromium now covers expert review completion/rework, admin deep publish/export/user mutations, and learner immersive listening/writing/speaking completion
  - remaining concerns are external signoff and coverage-depth risks, not active reproduced release blockers

## Severity Snapshot

| Severity | Open count | Resolved during audit |
| --- | ---: | ---: |
| Critical | 0 | 0 |
| High | 0 | 3 |
| Medium | 0 | 6 |
| Low | 0 known reproduced | 2 |

## Must-Fix Before Ship
- None currently known from the audited and reproduced defect set.

## Can-Ship-With Conditions
- Run the Playwright smoke suite and backend tests as a pre-release gate.
- Keep release controlled or staged rather than assuming zero residual risk.
- Treat cross-browser deep-mutation parity as a watch area until Firefox/WebKit expansion lands.
- Complete the external signoff gates below before final release approval.

## External Signoff Gates

### 1. Manual Assistive-Tech Signoff
- Required:
  - NVDA on Windows for sign-in, learner immersive flows, expert review completion, admin audit-log drawer/export, and admin user credit modal
  - VoiceOver on macOS or iOS for learner dashboard, learner settings/profile, and one immersive learner flow
- Execution checklist and evidence template:
  - `docs/qa/accessibility-report.md`
- Required evidence:
  - date performed
  - operator
  - assistive-tech and version
  - flow covered
  - pass/fail result
  - unresolved issues
- Current status:
  - pending external human execution

### 2. GitHub-Hosted `QA Smoke` Observation
- Workflow under observation:
  - `.github/workflows/qa-smoke.yml`
- Observation steps:
  1. push a branch or open a pull request containing the current workflow
  2. confirm the `QA Smoke` job runs on GitHub
  3. verify successful completion of:
     - checkout
     - Node.js setup
     - .NET setup
     - `npm ci`
     - Playwright browser install
     - backend tests
     - Docker smoke stack build/start
     - local stack readiness check
     - Playwright smoke suite
     - artifact upload
  4. verify `playwright-smoke-artifacts` uploaded successfully
  5. capture the GitHub Actions run URL
- Required evidence:
  - run URL
  - branch or PR identifier
  - pass/fail outcome
  - artifact upload confirmation
- Current status:
  - pending external GitHub-hosted observation

## Rollback Concerns
- Auth routing changes touch privileged sign-in and guard behavior:
  - rollback should revert the post-auth destination changes, layout role requirements, and hook redirects together
- MFA backend change touches recovery-code replacement logic:
  - rollback should be coordinated with the auth service only, not partially reverted in frontend alone
- Docker runtime change is low risk:
  - reverting `public/` copy would reintroduce browser/runtime asset failures
- New local listening-media fallback route supports the seeded immersive stack:
  - rollback would reintroduce local listening completion breakage for the seeded task

## Residual Risks
- The CI smoke workflow now exists in-repo, but it was not observed executing on GitHub during this local audit.
- Manual assistive-tech signoff is still pending.
- Deep expert/admin/immersive learner workflows are strongest in Chromium; Firefox/WebKit remain smoke-level for those mutations.
- Mock-player and diagnostic immersive completion are still lighter than the audited learner reading/listening/writing/speaking flows.
- No claim is being made that the application is defect-free.

## Recommended Next Actions
1. Run the manual NVDA and VoiceOver signoff checklist and store the evidence alongside the QA docs.
2. Observe one successful GitHub-hosted run of `QA Smoke` and record the run URL and artifact confirmation.
3. Expand Firefox/WebKit deep-mutation parity for expert review completion.
4. Expand Firefox/WebKit deep-mutation parity for admin deep CRUD/export.
5. Add controlled test-mail strategy to automate full verify-email and reset-password completion.
