# Incident Response and Secret Rotation Runbook

Security standard §14 (IR-01…IR-08). Status: **NOT YET EXERCISED** — the runbook below is written but IR-02 requires a tabletop test whose record is the evidence. An untested runbook is not a control.

## Roles

| Role | Owns | Named person |
|---|---|---|
| Incident lead | Declares severity, owns comms and the timeline | _unassigned_ |
| Payments owner | Payment state, provider contact, refund/fraud decisions | _unassigned_ |
| Platform owner | VPS, containers, DNS, WAF, database | _unassigned_ |
| Comms owner | Learner-facing messages, provider notices, legal/privacy | _unassigned_ |

Fill these in and keep them current (ARC-08). An unassigned role is itself a finding.

## Severity

| Level | Definition | Response target |
|---|---|---|
| S1 | Active payment fraud, confirmed account takeover of an admin, secret publicly leaked, database exposed | Immediate, all hands |
| S2 | Credential stuffing campaign, WAF bypass, single learner account compromised, provider webhook secret suspect | Same day |
| S3 | Reconnaissance, repeated failed logins, isolated abuse reports | Next business day |

## Universal first hour

1. **Preserve before changing anything.** Copy logs, raw webhook payloads, infrastructure events and the current build artifact hash to the evidence store. Redeploying or restarting over the top destroys the forensic trail (IR-03).
2. Identify the blast radius from the central logs: which accounts, which orders, which sessions, which keys.
3. Contain using the kill switches in the next section.
4. Only then remediate, and record every action with a timestamp.

## Kill switches and emergency controls

These are the levers that already exist in the product. A tabletop exercise must confirm each can actually be pulled.

| Situation | Action | Where |
|---|---|---|
| Payment fraud in progress | Disable the affected gateway so new checkouts cannot start | Admin → Runtime Settings → payment gateway enabled flags (`IPaymentGatewayCatalog.UpdateAsync`) |
| Forged or leaked webhook secret | Rotate the gateway's webhook secret; the shared handler already rejects `Processed: false` events, so a bad secret immediately fails closed | Runtime Settings → gateway webhook secret |
| Compromised admin account | Revoke sessions server-side; access tokens are rejected on the next request via the session-family check | Admin → Security → sessions, plus `ISessionRevocationService` |
| Compromised learner session | Force logout; revoked refresh families invalidate their access tokens | Admin → Security |
| Compromised signing key | Rotate `AuthTokens:AccessTokenSigningKey`; every issued token becomes invalid, forcing re-authentication | VPS env / Runtime Settings, then restart |
| Abusive traffic | Edge rate-limit tightening and managed challenge | See `runbook-waf-ddos.md` |
| Stuck or runaway AI spend | Disable the affected feature via feature flags (`ICompanionFeatureFlags` / AI kill switches fail closed) | Admin → Feature Flags |
| Bad deploy | Roll back to the previous blue/green slot | `scripts/deploy/rollout-release.sh` |

## Playbooks

### Account takeover (admin)
1. Revoke all sessions for the account (`ISessionRevocationService`); confirm the token is rejected afterwards.
2. Audit what the account did while compromised: `AuditEvents` for admin actions, `SecurityEvents` for auth events, and the payment webhook admin-retry trail.
3. Check whether MFA was disabled or recovery codes were redeemed (`MfaRecoveryCodes.RedeemedAt`).
4. Re-enrol MFA before restoring access, and rotate any credential the account could read.

### Payment fraud
1. Identify the affected transactions and confirm against the provider — never against a learner's screenshot.
2. Disable the gateway or the specific payment path; keep webhook ingestion alive so evidence keeps arriving.
3. Reconcile: run the reconciliation worker sweep and review every `reconciliation.mismatch` billing event.
4. Reverse entitlements only through the documented refund/dispute path so the audit trail stays intact (PAY-15).

### Secret leak
1. Determine the exposure window and whether the secret is still live.
2. Rotate immediately (see below) — deleting the file or commit is not remediation (DEV-04).
3. Check the secret-scan and provider logs for use during the exposure window.

### Database exposure
1. Confirm the exposure path (public port, leaked credential, or an application-level read bypass).
2. Close it, then rotate the database credentials and the application connection string.
3. Treat all data as disclosed for notification purposes and involve the comms/privacy owner.

### DDoS or load exhaustion
See `runbook-waf-ddos.md`. The origin-lock matters most: if the origin is reachable directly, no edge protection helps.

### Compromised mobile signing key
1. Rotate the Android keystore / Apple certificate.
2. Ship an update and use the existing forced-update mechanism to move users off the affected build.
3. Invalidate server-side any token or entitlement that the compromised key could have influenced.

## Secret rotation (IR-02)

For each secret: rotate, verify the new value is in use, confirm the old value no longer works, and record the time. Rotation must not require a prolonged outage — test that.

| Secret | Mechanism | Notes |
|---|---|---|
| JWT access/refresh signing keys | VPS env → restart | Invalidates all sessions; announce if planned |
| Payment gateway API keys + webhook secrets | Runtime Settings (DB override, no redeploy) | Rotate webhook secret and API key separately |
| Database password | VPS env + container restart | Update `POSTGRES_PASSWORD` and the connection string together |
| Brevo / SMTP credentials | Runtime Settings | |
| FCM service account / APNs key | Runtime Settings (encrypted columns) | |
| AI provider keys | Runtime Settings (`AiCredentialVault`) | |
| Backup encryption key | Offline, host-only | Losing it means losing the backups — store separately from the backups |

## Communications (IR-05)

Define in advance who may speak to: affected learners, the payment provider(s), the hosting provider, and legal/privacy advisers. Drafts for the two most likely messages — "we have identified unauthorised access to your account" and "we have identified an error in your billing" — should exist before they are needed.

## Tabletop exercise (IR-06)

Run at minimum: annually, after any major architecture change, and before each production launch. Rotate the scenario across admin takeover, payment fraud, secret leak, and database exposure. The record of the exercise — scenario, participants, decisions, and the runbook changes it produced — is the IR-06 evidence. **No exercise has been recorded yet.**

## Vulnerability disclosure (IR-07)

Publish a `security.txt` (or a security contact page) at `/.well-known/security.txt` on the marketing site so researchers can report safely. The website repo's `.htaccess` currently has no such path. A bug-bounty programme is optional and should wait until the team can triage reports.
