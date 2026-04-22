# Phase 3 — Expert / Admin / Sponsor JTBD & Journeys

## Artifacts
- `expert-review-queue-journey.md` + `expert-flow-review-item.md` + `expert-flow-calibration.md`
- `admin-content-publish-journey.md` + flows: ZIP import, paper edit, publish gate, bulk ops
- `admin-user-support-journey.md` + flows: search user, impersonate safely, refund, comp credits
- `sponsor-onboard-cohort-journey.md` + flow: seat provisioning, CSV invite, SSO hand-off
- `sponsor-roi-reporting-journey.md` + flow: progress dashboard, CSV export, procurement PDF

## Must include
- Role & permission surface — which of the 16 admin permissions gates each step.
- Audit events fired per step (tie back to `AuditEvent`).
- SLA expectations where applicable (review queue age, escalations).

## Exit criteria
- Every T0 route in Expert / Admin / Sponsor portals mapped to a journey step.
- Every flow names the SignalR hub events / notifications involved (where real-time).
