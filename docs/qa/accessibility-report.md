# Accessibility Report

## Scope
- Date: 2026-03-29
- Tooling:
  - `@axe-core/playwright`
  - Playwright keyboard interaction
  - browser accessibility/name inspection through user-facing locators
- Audited pages and interaction paths:
  - `/sign-in`
  - `/`
  - `/settings/profile`
  - `/expert/queue`
  - `/expert/review/writing/<disposable-review-request>`
  - `/admin/content`
  - `/admin/users/mock-user-001`
  - `/admin/audit-logs`
  - `/writing/player?taskId=wt-001`
  - `/speaking/task/st-001?mode=self`
  - `/speaking/results/<generated-id>`

## Automated Findings
- Final audited result:
  - no critical axe violations on audited pages
  - no serious axe violations on audited pages
- Fixed during this QA pass:
  1. progress indicators lacked accessible names
  2. compact/mobile home link lacked an accessible name
  3. several critical-flow text treatments had contrast risk in real browser rendering
  4. admin credit-adjustment modal dropped focus to `body` instead of returning it to the trigger on close
  5. admin audit-log drawer did not reliably restore focus to the invoking row on keyboard dismissal
  6. speaking results lacked a strong visible page heading for the audited result surface

## Keyboard Findings
- Sign-in page:
  - verified keyboard reachability to the primary submit button
  - no immediate keyboard trap detected on the audited auth surface
- Writing player:
  - leave-warning dialog opens, closes on `Escape`, and restores focus to the invoking trigger
  - submit-confirm dialog opens, closes on `Escape`, and restores focus to the invoking trigger
- Speaking task:
  - stop-practice dialog opens, closes on `Escape`, and restores focus to the invoking trigger
  - submit dialog opens, closes on `Escape`, and restores focus to the invoking trigger
- Admin:
  - credit-adjustment modal now restores focus to the launch trigger
  - audit-log drawer now restores focus to the invoking event row
- Expert review:
  - keyboard shortcuts for draft save (`Ctrl/Cmd+S`) and submit (`Ctrl/Cmd+Enter`) now behave reliably in the audited writing/speaking review paths
  - the rework prompt is an inline expansion, not a modal, so operability and validation were checked rather than dialog semantics

## Focus Findings
- Visible focus behavior was acceptable on the audited smoke and deep-workflow surfaces.
- Focus restoration is now explicitly covered for:
  - admin credit modal
  - admin audit-log drawer
  - writing leave dialog
  - writing submit dialog
  - speaking stop dialog
  - speaking submit dialog
- Remaining focus debt:
  - other expert/admin dialogs and drawers still need broader coverage
  - immersive learner players still need fuller tab-order validation beyond the audited stop/submit/leave flows

## Form Accessibility Findings
- Sign-in, forgot-password, reset-password, and settings/profile inputs use accessible labels in the audited flows.
- Writing player editor, expert rubric score selectors, rework reason input, admin credit form fields, and audit-log search all remained reachable through user-facing labels or roles in the audited tests.
- Remaining form debt:
  - full-form accessibility coverage is not yet automated for every settings subsection, admin content field permutation, or all expert reviewer edge states

## Landmarks, Names, and Semantics
- Headings and main-region assertions were stable on audited high-value pages.
- The mobile top-nav brand link now exposes an accessible name.
- Progress bars now expose meaningful accessible labels instead of generic or missing names.
- The speaking result surface now exposes a visible `Performance Summary` heading for both users and automated semantic checks.

## WCAG 2.2 Risk Notes
- Cleared on audited pages:
  - 1.1.1 Non-text Content risks addressed for audited link/progress naming
  - 1.4.x contrast-related red flags reduced on affected audited text
  - 2.1.1 Keyboard coverage improved on dialog-heavy learner/admin flows
  - 2.4.3 Focus Order and 2.4.7 Focus Visible improved on the audited modal/drawer dismiss paths
  - 4.1.2 Name, Role, Value improved on progress indicators and navigation link
- Still at risk outside the audited subset:
  - other dialog-specific focus management
  - full keyboard workflow depth in immersive tasks beyond the audited stop/submit/leave flows
  - larger-scale zoom/reflow auditing across every surface
  - real screen-reader announcement quality

## Manual-Style Accessibility Limitations
- No real NVDA or VoiceOver session was executed in this pass.
- No dedicated 200% zoom, reduced-motion, or forced-colors matrix was run across the full app.
- Accessibility confidence is good on the audited critical screens and audited modal-heavy paths, but not exhaustive across the whole route inventory.

## Human Assistive-Tech Signoff Checklist

### NVDA on Windows
- Sign-in:
  - verify form labels, error messages, submit state, and post-sign-in landing announcement
- Learner immersive flows:
  - reading player
  - listening player completion
  - writing player submit flow
  - speaking self-practice submit flow
- Expert review:
  - writing review final submit
  - speaking review final submit
  - inline rework prompt expansion and validation announcement
- Admin:
  - audit-log drawer open/close and export path
  - user credit-adjustment modal open/close and success message

### VoiceOver on macOS or iOS
- Learner dashboard
- Learner settings/profile
- One immersive learner flow:
  - writing player preferred
  - speaking task acceptable fallback

### Evidence Capture Template
- Date:
- Operator:
- Assistive technology and version:
- Environment:
- Flow covered:
- Result: `Pass` / `Fail`
- Notes:
- Unresolved issues:

## Unresolved Accessibility Debt
- Expert and admin dialog coverage is improved but not exhaustive.
- Full screen-reader validation remains a required human handoff step.
- Wider zoom/reflow and forced-colors coverage still needs a deliberate follow-up pass.
