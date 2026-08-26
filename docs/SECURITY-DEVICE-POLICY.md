# OET anti-sharing device policy

This is the exact device rule implemented for the course platform. It is the
source of truth for the learner sessions screen, the admin security console,
and the web, Android, iOS, Windows, and macOS clients.

## Default rule

- A learner has two approved client identities by default.
- A client identity is the stable value sent in `X-OET-Device-Id` and stored
  in `TrustedDevice.DeviceId`. Web clients persist it in both first-party
  `localStorage` and an opaque first-party cookie so privacy-oriented
  in-app-browser localStorage resets do not silently mint a new identity on
  every launch; native clients use OS secure storage. Browser profiles, apps,
  and installations remain separate identities; IP, country, tabs, and user-agent never create identity.
- The first enforced sign-in bootstraps one identity. A second distinct identity
  fills the second slot via the existing email-OTP device challenge without asking for a replacement target.
- When the two slots are full, a third distinct identity triggers a `replacement_required` outcome: the
  authenticated credential response returns masked registered-device choices, active/maximum counts, and a
  protected candidate-device challenge token. The learner must explicitly select which of the two
  approved identities to replace; the backend binds that choice to the challenge, sends the email OTP,
  and only then approves the new identity, revoking only the selected device and its session families.
- Revocation is also enforced by refresh-token family liveness, playback
  session revocation, and the global single-active-session invariant so that only the new session remains
  after approval; a missed realtime push is not an access bypass.

## Admin/Support trigger chart

| Trigger | Value |
|---|---|
| Identity key | Persisted `X-OET-Device-Id` (web: `localStorage` + shared first-party cookie; native/desktop: secure storage). Browser profiles, apps, and installations are separate identities; IP, country, tabs, and user-agent never create identity. |
| Threshold | Default `2`; Admin override `1-5` (`null` = default `2`). |
| Window | Runtime `DeviceChangeWindowDays`, default `7`. |
| Limit | Runtime `DeviceChangeMaxPerWindow`, default `3`. |
| Counted | OTP-approved replacements only; bootstrap is not counted. |
| Not counted | Same browser/app identity after an IP/location change or web storage recovery through the continuity cookie (`oet_device_binding`). |
| Reset | Admin device reset clears all approved identities and live sessions (`admin.device_reset`); learner recovery is password plus email OTP. |
| Cooldown evidence | `cooldownUntil`, exact `secondsRemaining`, configured `window`/`limit`, and live human-readable `countdown` (e.g. `2h 13m 5s`). Learners retain the secure email-OTP recovery route; privileged accounts receive the exact cooldown error and Admin reset remains the recovery path. |

## What counts as one identity

| Client surface | Counting rule |
|---|---|
| Web browser | One browser profile and its tabs/windows count as one. Clearing all site data or using another browser profile creates a new identity. The web identity is backed by localStorage plus a first-party cookie. |
| Android/iOS official app | One app installation and its OS secure-storage record count as one. Uninstalling/reinstalling creates a new identity. |
| Windows/macOS official desktop app | One app installation and its persisted desktop identity count as one. |
| Same physical computer/phone | A browser profile and an official app count separately. The server intentionally does not infer hardware equivalence from user-agent, IP, or platform headers. |

The server counts the stable client identity, not tabs, windows, user-agent
strings, IP addresses, or a claimed physical serial number. The platform
header is normalized to `web`, `capacitor-android`, `capacitor-ios`, or
`desktop`; it provides context but does not define identity.

## Admin override

An admin may set a positive per-learner override from 1 through 5 approved
identities. `null` means the strict default of two. There is no unlimited
setting. Lowering the limit immediately revokes the oldest identities that no
longer fit and revokes their associated session families.

The override changes how many identities may remain approved; it does not
disable the global single-active-session invariant (enforced whenever trusted
device policy is active). Therefore, an account with an override greater than
one may avoid re-approving retained identities, but it still cannot be used
simultaneously in two live sessions.

The rolling `DeviceChangeWindowDays` / `DeviceChangeMaxPerWindow` cooldown is
separate from the approved-identity limit and counts OTP-approved replacement
identities only; the initial bootstrap does not consume the change budget. An
override does not make unlimited rapid device churn possible. For learner
accounts, reaching that counter routes the already-authenticated password
attempt through the existing email-OTP device challenge (with exact `cooldownUntil`, `secondsRemaining`, window/limit, and countdown evidence) instead of leaving the
learner at a support-only dead end. Privileged accounts retain the hard
cooldown block with the same exact evidence and Admin reset remains the recovery path.

## Audit and user-visible evidence

The security feed records the lifecycle with these event kinds:

- `device.trust_requested`, `device.trusted`, `device.trust_rejected`
- `device.revoked`, `device.change_blocked_cooldown`, `device.admin_reset`
- `admin.device_limit_override` and `session.revoked`

Device approval, rejection, automatic replacement/reduction revocation, and
system-triggered sign-out also create `AuditEvent` records. Admin changes and
manual revocations retain the acting admin in the existing audit trail. The
admin security page lists the event kinds and links to Audit Logs.

The sign-in page explains these policy-driven sign-outs:

- a replacement at the one-identity limit;
- a replacement when an override is full; and
- an administrator reduces the limit or revokes a device.

## Recovery

An admin can reset all approved identities from the learner's Sessions &
Devices panel. Resetting is a security-boundary operation: it revokes active
sessions and requires the next sign-in to bootstrap a new identity. A learner
who reaches the rolling cooldown can recover through the password plus email
OTP challenge; the admin reset remains available when the approved identity
itself must be cleared or the account has lost access to its normal device.
