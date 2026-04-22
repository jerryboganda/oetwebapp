# UX Audit Master Plan — OET Prep Platform (A → Z, 100%)

> **Scope:** All 4 portals (Learner, Expert, Admin, Sponsor) • 217 routes • Web + Desktop (Electron) + Mobile (iOS/Android Capacitor)
> **Goal:** Detect every UX gap, map every user journey, produce Figma-ready artifacts, and close 100% of gaps through phased delivery.
> **Owner:** UX Research + Design
> **Status:** Phase 0 (Planning) complete. Phases 1–8 scheduled.
> **References:** [AGENTS.md](../../AGENTS.md) · [DESIGN.md](../../DESIGN.md) · [mobile-ui-ux-audit-report.md](../mobile-ui-ux-audit-report.md) · [COMPREHENSIVE_SYSTEM_GAP_ANALYSIS.md](../COMPREHENSIVE_SYSTEM_GAP_ANALYSIS.md)

---

## 1. Audit Objectives

1. **Identify every UX gap** across 217 routes — visual, interaction, content, accessibility, performance-perceived, and journey-level.
2. **Map every primary user journey** using Jobs-to-be-Done (JTBD) framing so design decisions are user-outcome-driven, not feature-driven.
3. **Produce Figma-ready artifacts** (JTBD statements, journey maps, flow specs, a11y checklists, content rewrites) for each portal.
4. **Deliver a prioritized remediation backlog** (Critical → Major → Minor → Enhancement) with acceptance criteria.
5. **Institute ongoing governance** (design tokens, heuristic review cadence, accessibility gates) so regressions do not reappear.

---

## 2. Personas (Working Set)

| # | Persona | Portal | Primary JTBD |
|---|---------|--------|--------------|
| P1 | **Nurse Nadia** — IELTS-overseas nurse, shift-worker, mobile-first | Learner | "When I have 20 minutes between shifts, I want to practise OET writing with instant scoring so I can hit 350 on Writing before my visa deadline." |
| P2 | **Doctor Dinesh** — GP relocating, desktop-at-night, mixed devices | Learner | "When I've booked my test in 6 weeks, I want a plan that tells me exactly what to do daily so I know I'll pass all four sub-tests." |
| P3 | **Student Sana** — mid-funnel free-tier, price-sensitive | Learner (free) | "When I'm deciding whether to pay, I want to try a realistic mock and see a trustworthy score so I can justify the subscription." |
| P4 | **Expert Emma** — OET marker, paid per item, quality-audited | Expert | "When I pick up a review queue, I want all candidate evidence in one place with the rubric inline so I can grade consistently without app-switching." |
| P5 | **Admin Arman** — platform operator, content + ops + support | Admin | "When content or a learner needs attention, I want to find, fix, and audit it in ≤3 clicks without breaking publish gates." |
| P6 | **Sponsor Sam** — hospital/agency bulk buyer | Sponsor | "When I'm onboarding 40 nurses, I want to provision seats, monitor progress, and prove ROI to procurement in one dashboard." |

---

## 3. Audit Heuristics (Scoring Rubric)

Each page is scored **0–3** across 10 dimensions (0 = fail, 3 = exemplary). Max 30. < 20 → remediation required.

| # | Heuristic | Key Questions |
|---|-----------|---------------|
| H1 | **Clarity of purpose** | Does the user know what this page is for in < 3 seconds? |
| H2 | **Primary action obvious** | Is the #1 CTA visually dominant and above the fold? |
| H3 | **Content scannable** | F-pattern, hierarchy, no walls of text, no dev copy. |
| H4 | **State coverage** | Empty, loading, error, partial, success — all handled. |
| H5 | **Feedback & progress** | Every user action confirms within 100 ms; long tasks show progress. |
| H6 | **Error recovery** | Errors are diagnostic + actionable, not just "something went wrong". |
| H7 | **Consistency** | Matches design tokens, motion system, copy voice. |
| H8 | **Accessibility** | WCAG 2.2 AA: keyboard, focus, contrast, SR labels, touch targets ≥ 44 px. |
| H9 | **Mobile parity** | Works on 360 px viewport without horizontal scroll or clipped CTAs. |
| H10 | **Trust & credibility** | Real data, no lorem ipsum, no internal jargon, pricing/scoring transparent. |

---

## 4. Phased Delivery Plan

> **Principle:** Each phase produces shippable artifacts. No phase blocks another more than 20%.

### **Phase 0 — Planning & Governance** ✅ *(this document)*
Deliverables:
- `docs/ux/UX-AUDIT-MASTER-PLAN.md` (this file)
- `docs/ux/UX-AUDIT-ROUTE-INVENTORY.md` — full 217-route inventory with owners
- `docs/ux/UX-AUDIT-HEURISTICS.md` — scoring rubric + template
- Kickoff alignment with eng, content, product

### **Phase 1 — Discovery & Evidence Gathering**
Goal: Capture current reality without changing it.
Deliverables:
- `docs/ux/phase-1/route-screenshots/` — light + dark, desktop + mobile, 3 auth roles
- `docs/ux/phase-1/content-inventory.md` — every user-visible string, flagged for dev copy
- `docs/ux/phase-1/a11y-baseline.md` — axe-core + Lighthouse runs per portal
- `docs/ux/phase-1/analytics-baseline.md` — funnel drop-offs from `lib/analytics.ts`
- `docs/ux/phase-1/support-tickets-themes.md` — top 20 friction patterns from support

### **Phase 2 — JTBD & Journey Mapping (Learner Portal)**
Deliverables per persona P1–P3:
- `docs/ux/phase-2/learner-<persona>-jtbd.md`
- `docs/ux/phase-2/learner-<persona>-journey.md` (Awareness → Exploration → Action → Outcome → Retention)
- `docs/ux/phase-2/learner-flow-<flow>.md` for: onboarding, diagnostic, mock, writing submit, speaking session, billing, referral, result review
- Accessibility requirements embedded in each flow

### **Phase 3 — JTBD & Journey Mapping (Expert, Admin, Sponsor)**
Deliverables:
- `docs/ux/phase-3/expert-review-queue-journey.md` + flow
- `docs/ux/phase-3/expert-calibration-journey.md` + flow
- `docs/ux/phase-3/admin-content-publish-journey.md` + flow
- `docs/ux/phase-3/admin-user-support-journey.md` + flow
- `docs/ux/phase-3/sponsor-onboard-cohort-journey.md` + flow
- `docs/ux/phase-3/sponsor-roi-reporting-journey.md` + flow

### **Phase 4 — Heuristic Evaluation (All 217 Routes)**
Deliverables:
- `docs/ux/phase-4/scorecard.csv` — one row per route, 10 heuristic scores, total, severity
- `docs/ux/phase-4/gap-register.md` — every gap with ID `UX-<portal>-<nnn>`, severity, owner, acceptance
- `docs/ux/phase-4/critical-fix-list.md` — launch-blockers only

### **Phase 5 — Content & Copy Audit**
Deliverables:
- `docs/ux/phase-5/voice-and-tone.md` — canonical voice per portal
- `docs/ux/phase-5/copy-rewrites.md` — old → new for every flagged string
- `docs/ux/phase-5/empty-error-loading-copy.md` — canonical state copy library
- `docs/ux/phase-5/microcopy-glossary.md` — OET-specific terms (scaled, raw, CBLA, etc.)

### **Phase 6 — Accessibility Remediation Plan**
Deliverables:
- `docs/ux/phase-6/a11y-gap-register.md` — WCAG 2.2 AA failures per route
- `docs/ux/phase-6/keyboard-map.md` — tab-order + shortcuts per key flow
- `docs/ux/phase-6/sr-script.md` — screen-reader walk-through for top 10 flows
- `docs/ux/phase-6/color-contrast-report.md` — token-level fixes

### **Phase 7 — Design System & Motion Consistency**
Deliverables:
- `docs/ux/phase-7/token-audit.md` — usages of non-token hex/px values
- `docs/ux/phase-7/component-inventory.md` — every UI component, variant usage, drift
- `docs/ux/phase-7/motion-consistency.md` — route/list/overlay timing conformance
- `docs/ux/phase-7/figma-library-sync.md` — what to build/update in Figma

### **Phase 8 — Remediation Backlog & Rollout**
Deliverables:
- `docs/ux/phase-8/backlog.md` — prioritized tickets with JTBD link, acceptance criteria, severity
- `docs/ux/phase-8/rollout-plan.md` — batched releases, risk, dependencies
- `docs/ux/phase-8/validation-plan.md` — pre/post metrics, usability tests, success criteria
- `docs/ux/phase-8/governance-playbook.md` — ongoing cadence: weekly heuristic review, a11y gate in CI, design-review checklist in PR template

---

## 5. Severity Definitions

| Severity | Definition | SLA |
|----------|------------|-----|
| 🔴 **Critical** | Blocks core JTBD, exposes internal copy, a11y failure on critical path, data loss risk, broken route on public launch surface. | Fix before next release |
| 🟠 **Major** | Degrades primary journey by > 30%, inconsistent state handling, visible dev copy on secondary surface, missing mobile parity. | Fix within 2 releases |
| 🟡 **Minor** | Cosmetic drift, minor copy polish, non-critical a11y (labels on rarely-used controls). | Fix within quarter |
| 💡 **Enhancement** | Net-new improvement (animation polish, empty-state illustrations, delight). | Backlog, prioritize by impact |

---

## 6. Definition of Done (Per Gap)

A gap `UX-<portal>-<nnn>` is closed only when:
1. **Spec** — gap entry has clear acceptance criteria + linked JTBD / journey step.
2. **Design** — Figma frame exists (or explicit "code-only" tag), tokenised, responsive 360/768/1280.
3. **A11y** — keyboard path verified, SR label audited, contrast ≥ 4.5:1 (text) / 3:1 (UI).
4. **Build** — implemented behind type-checked code, no `any`, no `@ts-ignore`.
5. **Test** — unit (Vitest) + E2E (Playwright) where behaviour changed.
6. **Validation** — reviewed against heuristics scorecard; score ≥ 24/30 post-fix.
7. **Telemetry** — where funnel-relevant, event added via `lib/analytics.ts`.

---

## 7. Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Scope explosion across 217 routes | Route inventory with severity-first triage; Phases 4–8 work from the critical list first. |
| Concurrent feature work regresses UX | Add a design-review checklist to PR template (Phase 8). |
| Mobile and desktop drift | Every flow spec lists 360/768/1280 requirements; Capacitor-specific notes flagged. |
| Content-team copy drift | Phase 5 microcopy glossary + voice guide; add `lint-copy` list of forbidden dev phrases. |
| A11y regressions | Phase 6 CI gate (axe on smoke routes). |

---

## 8. Success Metrics

Measured at Phase 8 close, compared to Phase 1 baseline:
- **Critical gaps:** 0 open (from Phase 4 baseline).
- **Average heuristic score:** ≥ 24/30 across all routes (was TBD).
- **Task success rate** (top 8 JTBDs, moderated test, n ≥ 5): ≥ 90%.
- **WCAG 2.2 AA:** 100% critical-path routes pass axe with 0 serious violations.
- **Mobile parity:** 0 horizontal-scroll issues at 360 px on top 50 routes.
- **Funnel conversion:** free → paid, diagnostic → plan, mock → retry — measured deltas reported.
- **Support ticket themes:** top 5 friction themes each reduced ≥ 40% in 90 days.

---

## 9. Next Actions (Immediate)

1. Create Phase 0 companions: route inventory + heuristics template.
2. Phase 1 discovery kickoff — screenshot capture script + axe batch runner.
3. Persona validation with 2 real users per persona (P1, P4, P6 priority).
4. Stand up `docs/ux/phase-X/` skeletons so contributors can drop evidence as they find it.

