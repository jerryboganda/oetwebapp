# Speaking Module — Release Checklist

Every gate below must be green before flipping `Features__SpeakingV2 = true` for a cohort.

## A. Tests

- [ ] `dotnet test backend/OetWithDrHesham.sln --filter "FullyQualifiedName~Speaking"` — green
- [ ] `npm test` — green
- [ ] `npm run lint` — green
- [ ] `npx tsc --noEmit` — green
- [ ] Playwright nightly (`speaking-e2e.yml`) — last 3 runs green
- [ ] Axe nightly (`speaking-a11y.yml`) — no serious/critical violations open

## B. Performance

- [ ] k6 weekly run within SLOs (see `docs/speaking/sla.md`)
- [ ] Anthropic prompt-cache hit rate ≥ 80%
- [ ] LiveKit egress round-trip ≤ 12s after `room_finished`

## C. Content

- [ ] Profession reference catalog covers top 12 OET professions
- [ ] ≥ 32 published role-play cards (8/profession × 4 priority professions)
- [ ] ≥ 30 published drills covering all 11 drill kinds (spec §17)
- [ ] ≥ 10 published mock sets
- [ ] ≥ 5 calibration samples with gold scores

## D. Compliance

- [ ] `SpeakingComplianceConsent` records flowing for every session
- [ ] Recording deletion writes `AuditEvent`
- [ ] Retention worker honors tutor-reviewed vs default windows
- [ ] `ScoreDisclaimer` rendered prominently on every results surface
- [ ] GDPR erasure pre-flight endpoint returns expected inventory

## E. Documentation

- [ ] Runbook reviewed (`docs/speaking-module-runbook.md`)
- [ ] Architecture docs reviewed (`docs/speaking/architecture.md`, diagrams)
- [ ] Threat model reviewed + checklist signed off (`docs/security/speaking/`)
- [ ] SLA published (`docs/speaking/sla.md`)
- [ ] Incident runbook on-call validated (`docs/speaking/incident-runbook.md`)

## F. Operational

- [ ] LiveKit Cloud project provisioned + S3 egress bucket created
- [ ] Anthropic + OpenAI keys rotated within last 90 days
- [ ] On-call rota set (primary + secondary)
- [ ] Grafana dashboards imported (`ops/dashboards/speaking-*.json`)
- [ ] Alert routes configured (PagerDuty / Slack)

## G. Sign-off

- [ ] Speaking lead
- [ ] Backend lead
- [ ] Design lead
- [ ] Security lead
- [ ] Compliance lead
- [ ] Tutor-ops lead

## Cohort rollout

1. Staging — internal smoke for 48h
2. Production 5% — 7 days monitored
3. Production 25% — 7 days monitored
4. Production 100% — feature flag deprecated 30 days later
