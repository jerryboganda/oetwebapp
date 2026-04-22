# UX Audit — Heuristic Scorecard Template

> Companion to [UX-AUDIT-MASTER-PLAN.md](./UX-AUDIT-MASTER-PLAN.md).
> Score every audited route 0–3 on 10 heuristics. Max 30. Any route < 20 enters the fix queue.

---

## How to Score

| Score | Meaning |
|-------|---------|
| 0 | Fails outright — blocks user or is absent. |
| 1 | Present but broken on a core path. |
| 2 | Works but drifts from standard / minor friction. |
| 3 | Exemplary — matches spec, delights where appropriate. |

---

## Heuristic Definitions

### H1 — Clarity of purpose
- **3**: Hero / title tells me the job in ≤ 5 words. Sub-text earns attention.
- **1–2**: Jargon, OET-insider language without explanation, ambiguous header.
- **0**: Dev copy visible ("Keep the launch cards…"), no header at all.

### H2 — Primary action obvious
- **3**: One dominant CTA, brand colour, top of fold, verb-led label.
- **1–2**: Multiple equal buttons, secondary styled like primary.
- **0**: No CTA or CTA below fold on 360-px viewport.

### H3 — Content scannable
- **3**: F-pattern hierarchy, ≤ 60-char line length, chunks ≤ 3 lines, bullets where appropriate.
- **0**: Wall of text, HTML-pasted content, inline-styled prose.

### H4 — State coverage
Check: **empty · loading · error · partial · success**. One point per two states present and appropriate.
- **3**: All five handled with brand-correct copy & visuals.
- **0**: Blank screen on empty, browser spinner on load, raw error message.

### H5 — Feedback & progress
- **3**: Optimistic UI or < 100 ms ack; long tasks show progress + cancellable where safe.
- **0**: Silent submits, no success confirmation, no loading state on async.

### H6 — Error recovery
- **3**: Plain-language cause + next step + link to support. Not "Error 500".
- **0**: Raw stack, modal without dismiss, user stuck.

### H7 — Consistency
- **3**: Uses design tokens (`lib/motion.ts`, Tailwind tokens), `Button` variant `primary`, `Badge` variant `danger`, motion from `motion/react`.
- **0**: Inline hex colours, bespoke components, `framer-motion` imports, variants like `destructive`/`default`.

### H8 — Accessibility (WCAG 2.2 AA minimum)
- **3**: Keyboard path verified, SR labels correct, focus visible (not just browser default), contrast ≥ 4.5:1 text / 3:1 UI, touch targets ≥ 44 px, semantic HTML.
- **0**: Keyboard trap, missing labels, contrast < 3:1.

### H9 — Mobile parity
- **3**: No horizontal scroll at 360 px, CTAs above fold, tap targets spaced, safe-area insets respected on iOS.
- **0**: Clipped content, button off-screen, nav overlaps content.

### H10 — Trust & credibility
- **3**: Real strings, no "Lorem", no internal tags, scoring/pricing transparent, citations/links present where claims are made.
- **0**: Placeholder data, contradictory claims, unclear pricing, missing legal/privacy cues.

---

## Scorecard Template (copy per route)

```md
### Route: `/<path>`
- **Portal:** learner | expert | admin | sponsor | auth
- **Persona(s):** P1 / P2 / …
- **Tier:** T0 / T1 / T2 / T3
- **Date audited:** YYYY-MM-DD · **Auditor:** <name>
- **Evidence:** screenshots/phase-1/<route>/{light,dark}-{mobile,desktop}.png

| Heuristic | Score | Notes |
|-----------|-------|-------|
| H1 Clarity of purpose     |   | |
| H2 Primary action obvious |   | |
| H3 Content scannable      |   | |
| H4 State coverage         |   | |
| H5 Feedback & progress    |   | |
| H6 Error recovery         |   | |
| H7 Consistency            |   | |
| H8 Accessibility          |   | |
| H9 Mobile parity          |   | |
| H10 Trust & credibility   |   | |
| **Total (/30)**           |   | |

**Severity:** 🔴 Critical · 🟠 Major · 🟡 Minor · 💡 Enhancement

**Gaps identified** (map to `gap-register.md`):
- UX-<portal>-NNN — <one line> — severity — acceptance: <criterion>

**Linked JTBD:** <journey step>
**Linked flow spec:** `docs/ux/phase-2|3/<flow>.md`
```

---

## CSV Schema (Phase 4)

`docs/ux/phase-4/scorecard.csv` columns:

```
route,portal,persona,tier,auditor,date,H1,H2,H3,H4,H5,H6,H7,H8,H9,H10,total,severity,gap_ids,notes
```

One row per route. Derived dashboards group by portal and severity to drive Phase 8 backlog.

---

## Gap ID Scheme

`UX-<portal>-<nnn>` where portal ∈ {`learner`, `expert`, `admin`, `sponsor`, `auth`, `global`}.
Example: `UX-learner-007` — "Sign-in page lacks visible focus ring on MFA input on dark mode".

Each gap in `gap-register.md` carries:
- Severity · Route(s) · Heuristic(s) hit · Owner · JTBD link · Acceptance criteria · Test plan · Status.

