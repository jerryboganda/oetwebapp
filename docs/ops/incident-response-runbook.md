# Incident Response Runbook

This runbook covers launch-blocking incidents for auth, billing, AI/provider flows, PII/audio storage, uploads, and deployment failures.

## Roles

- Incident commander: **Dr Faisal Maqsood** (primary)
- Engineering lead: **Dr Faisal Maqsood** (escalation contact for this release)
- Security/privacy owner: **Dr Faisal Maqsood**
- Customer communication owner: **Dr Faisal Maqsood**
- Scribe: incident commander appoints at incident open

> Single-owner rotation accepted for the v1 launch (decision recorded 2026-05-10 against RW-021). Re-evaluate at first hire or first SEV-1.

## Tabletop Exercises (run before launch)

1. **Auth compromise** — simulated leak of an admin refresh token. Drill the
   refresh-family revocation path (`AuthService.RevokeRefreshTokenFamily`),
   confirm forced sign-out propagates, audit the `AuditEvent` rows, and rotate
   `Auth__JwtSecret` / `Auth__RefreshTokenSecret` via the `.env.production`
   handoff procedure in DEPLOYMENT.md.
2. **Provider outage** — force the active AI provider account into
   `IsActive = false` via the admin AI config console. Confirm the gateway
   fails closed with a deterministic fallback (writing `ScoreRange = "pending"`
   + rule-engine findings; speaking + conversation surface advisory copy)
   and that no learner-visible payload contains raw provider errors or keys.
3. **PII / audio retention failure** — simulate the
   `ConversationAudioRetentionWorker` / `PronunciationAudioRetentionWorker`
   failing to sweep. Drill the manual sweep procedure, verify
   `ConversationOptions.AudioRetentionDays` / `PronunciationOptions.AudioRetentionDays`
   bounds, and confirm an `AuditEvent` row is written for the corrective
   delete.


## Severity Triggers

| Severity | Trigger examples | Response target |
| --- | --- | --- |
| SEV-1 | production unavailable, auth compromise, payment impact, PII/audio exposure | immediate triage |
| SEV-2 | major feature outage, provider outage without fallback, upload pipeline failure | same business day |
| SEV-3 | degraded route, non-critical regression, observability gap | next triage window |

## Triage Checklist

1. Confirm incident start time, affected routes, affected personas, and current release SHA.
2. Preserve evidence: logs, deployment artifact links, SBOM/SCA artifacts, screenshots, request IDs, and provider status.
3. Identify whether the issue is code, infrastructure, provider, data, auth/RBAC, or configuration.
4. Decide: mitigate in place, disable feature flag/provider route, roll back, or hold.
5. Record all decisions with owner and timestamp.

## Evidence Commands

```bash
git rev-parse HEAD
git log --oneline -1
docker ps --no-trunc
docker images --format 'table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.CreatedSince}}'
docker compose --env-file .env.production -f docker-compose.production.yml ps
```

Use `scripts/deploy/pre-flight.sh` before risky remediation when the production host is stable enough to snapshot. Use `scripts/deploy/post-deploy-verify.sh` after rollback or hotfix.

## Containment Options

- Disable provider route or feature flag from the admin/provider console.
- Stop new uploads while preserving existing files.
- Revoke compromised refresh-token families.
- Put affected provider-backed flows into deterministic fallback mode.
- Roll back the deployed image/tag if the current release is the cause.

## Postmortem Template

- Incident title:
- Severity:
- Start/end time UTC:
- Customer impact:
- Root cause:
- Detection source:
- What worked:
- What failed:
- Follow-up actions:
- Owners and target dates:
