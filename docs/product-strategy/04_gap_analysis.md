# 04 - Gap Analysis

## Status model used in this document

- Strong: working well and strategically aligned.
- Incomplete: shipped foundation, but important business or trust gaps remain.
- Partial: meaningful pieces exist, but the product is not coherent end to end.
- Missing: not meaningfully present today.
- Blocked: should not be built until a prerequisite is solved.
- Opportunity: not expected yet, but high strategic upside.
- Defer: not worth near-term focus.

## Gap table

| Capability | Layer | Status | Business value | Learner impact | Trust impact | Subscription impact | Cost | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Auth, profile, role routing | Shared core | Strong | High | High | High | Medium | Low | Mature enough to support ongoing expansion. |
| Learner four-skill surface | Shared core | Strong | High | High | Medium | High | Medium | Strong route coverage already exists. |
| Expert review operations | Shared core + OET | Strong | High | High | High | High | Medium | Queue, workspaces, calibration, and admin review ops are already live. |
| Billing catalog and wallet domain | Shared core | Strong | High | Medium | High | High | Medium | Rich domain already exists. |
| Production payment reliability | Shared core | Incomplete | High | Medium | High | High | Medium | Hosted checkout and webhook handling are now much better, but live credential rollout, monitoring, and reconciliation still need operational completion. |
| Strict entitlement enforcement across all premium surfaces | Shared core | Incomplete | High | Medium | High | High | Medium | Billing state is strong, but every premium route and action must remain aligned with entitlements. |
| Exam-family-aware score validation | Shared core | Incomplete | High | High | High | Medium | Medium | Improved in this session, but more readiness/reporting logic still assumes OET. |
| Exam-family-aware learner copy and reporting | Shared core | Partial | Medium | High | Medium | Medium | Medium | Improved in key pages, but not yet fully systematic. |
| AI confidence, provenance, escalation visibility | Shared core | Incomplete | High | High | High | High | Medium | Key learner summaries now expose trust cues; admin-wide reporting and policy visibility still need depth. |
| Content revision history | Shared core | Strong | Medium | Medium | High | Medium | Low | Good base exists. |
| Content provenance and QA analytics | Shared core | Partial | High | Medium | High | Medium | Medium | Needs structured authoring provenance, stale-content review, and performance loops. |
| OET profession-specific writing coaching | OET flagship | Incomplete | High | High | High | High | Medium | Core flows exist; remediation and compare loops should go deeper. |
| OET speaking role-play and transcript coaching | OET flagship | Incomplete | High | High | High | High | Medium | Strong foundation exists; phrase coaching and readiness blockers should be stronger. |
| OET expert SLA and QA visibility | OET flagship | Incomplete | High | Medium | High | High | Medium | Operators need clearer SLA and quality signals for premium trust. |
| IELTS operational content and reporting layer | IELTS future layer | Missing | High | High | Medium | High | Medium | Shared core is ready enough; task-specific scoring/reporting still needs implementation. |
| IELTS Academic vs General divergence | IELTS future layer | Missing | Medium | High | Medium | Medium | Medium | Must be explicit in writing tasks and scoring logic. |
| PTE question-type engine | PTE future layer | Missing | Medium | High | Medium | High | High | Should be built as a dedicated engine, not a thin exam toggle. |
| PTE rapid-drill and AI-first analytics | PTE future layer | Opportunity | Medium | High | Medium | High | High | Large upside, but only after PTE-native architecture. |
| Desktop/mobile shell expansion | Packaging layer | Defer | Low | Medium | Low | Low | Medium | Keep maintained, but not a primary growth lever right now. |
| Unauthenticated route network noise in smoke E2E | Shared core | Incomplete | Low | Low | Medium | Low | Low | Current smoke run shows protected-route redirects still emit a small set of 401 preload calls. |

## Highest-priority gaps

### 1. Payment reliability and entitlement strictness

Why it matters:

- directly affects revenue capture
- directly affects refund and support burden
- directly affects trust in premium review and add-on products

### 2. Exam-family abstraction cleanup

Why it matters:

- IELTS cannot launch cleanly while OET-only assumptions remain in score logic and learner copy
- shared-core credibility depends on this layer being real, not cosmetic

### 3. AI trust boundary hardening

Why it matters:

- AI-assisted scoring is commercially useful only if learners understand its confidence and limits
- premium human review works better when escalation is visible and defensible

### 4. OET flagship deepening

Why it matters:

- OET is the clearest near-term monetization and differentiation advantage
- the current platform already has enough foundation to become best-in-class here faster than it can become broadly best-in-class everywhere

### 5. Content operations maturity

Why it matters:

- multi-exam expansion without provenance and QA discipline will create noise, not scale
- content performance must become measurable before authoring volume increases

## Shared-core vs exam-specific interpretation

### Shared core

Must be generalized now:

- auth and profile
- exam-family selection and score validation
- diagnostics and study planning
- evaluation trust labels
- billing and entitlements
- notifications and analytics
- admin governance and audit

### OET-specific

Must be deepened now:

- profession taxonomy value in productive tasks
- writing revision coaching
- speaking clinical communication loops
- expert review premium experience
- readiness blockers tied to productive-skill evidence

### IELTS-specific

Should be built next:

- Academic and General writing task models
- IELTS band-aware reporting language
- IELTS-specific writing and speaking criteria mapping

### PTE-specific

Should be intentionally deferred until designed properly:

- question-type drill engine
- integrated-skill timing and scoring model
- app-like rapid practice and analytics loops

## Gap-analysis conclusion

The biggest strategic mistake would be treating missing features and stale assumptions as the same thing.

This product is already strong in many foundational areas. The highest-value work is to:

- harden what already exists
- remove OET-only constraints from the shared core
- deepen OET where users will pay for premium certainty
- then operationalize IELTS from that cleaner base
