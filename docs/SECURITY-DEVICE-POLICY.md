# OET anti-sharing device policy

This is the exact device rule implemented for the course platform. It is the
source of truth for the learner sessions screen, the admin security console,
and the web, Android, iOS, Windows, and macOS clients.

## Default rule

- A learner has one approved client identity by default.
- A client identity is the stable value sent in `X-OET-Device-Id` and stored
  in `TrustedDevice.DeviceId`.
- The first enforced sign-in bootstraps one identity. A different identity must
  complete the existing email-OTP device challenge before it is approved.
- When a new identity is approved while the default one-identity limit is
  full, the previous identity is revoked and all of its live session families
  are revoked. The previous client receives a `session_revoked` message that
  explains that a newer device was approved.
- Revocation is also enforced by refresh-token family liveness and playback
  session revocation, so a missed realtime push is not an access bypass.

## What counts as one identity

| Client surface | Counting rule |
|---|---|
| Web browser | One browser profile and its tabs/windows count as one. Clearing site storage or using another browser profile creates a new identity. |
| Android/iOS official app | One app installation and its OS secure-storage record count as one. Uninstalling/reinstalling creates a new identity. |
| Windows/macOS official desktop app | One app installation and its persisted desktop identity count as one. |
| Same physical computer/phone | A browser profile and an official app count separately. The server intentionally does not infer hardware equivalence from user-agent, IP, or platform headers. |

The server counts the stable client identity, not tabs, windows, user-agent
strings, IP addresses, or a claimed physical serial number. The platform
header is normalized to `web`, `capacitor-android`, `capacitor-ios`, or
`desktop`; it provides context but does not define identity.

## Admin override

An admin may set a positive per-learner override from 1 through 5 approved
identities. `null` means the strict default of one. There is no unlimited
setting. Lowering the limit immediately revokes the oldest identities that no
longer fit and revokes their associated session families.

The override changes how many identities may remain approved; it does not
disable the global single-active-session invariant (enforced whenever trusted
device policy is active). Therefore, an account with an override greater than
one may avoid re-approving retained identities, but it still cannot be used
simultaneously in two live sessions.

The rolling `DeviceChangeWindowDays` / `DeviceChangeMaxPerWindow` cooldown is
separate from the approved-identity limit. An override does not make unlimited
rapid device churn possible.

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
sessions and requires the next sign-in to bootstrap a new identity. The
cooldown remains a separate control and can be cleared only through the
admin recovery action.
