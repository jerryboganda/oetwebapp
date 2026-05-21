# Speaking Module — Key Rotation Procedure

Rotation cadence: **every 90 days** for every external provider key. Zero-downtime via dual-deploy → switch → retire.

## Inventory

| Key | Where stored | Rotated by |
|-----|--------------|------------|
| `ANTHROPIC__APIKEY` | env / secret manager | Speaking lead |
| `OPENAI__APIKEY` | env / secret manager | Speaking lead |
| `ELEVENLABS__APIKEY` | env / secret manager | Speaking lead |
| `WHISPER__APIKEY` | env / secret manager (or shared with `OPENAI__APIKEY`) | Speaking lead |
| `LIVEKIT__APIKEY` + `LIVEKIT__APISECRET` | env / secret manager | Ops lead |
| `LIVEKIT__WEBHOOKSIGNINGSECRET` | env / secret manager | Ops lead |
| `AWS__ACCESSKEYID` + `AWS__SECRETACCESSKEY` | env / secret manager (or IAM role on prod) | Ops lead |

## Standard procedure (any key)

1. **Issue new** — generate a new key at the provider console. Note the issued-at timestamp.
2. **Dual-deploy** — add the new key as `<KEY>_NEXT` env. Backend reads `<KEY>_NEXT` first when present, falls back to the original. Deploy.
3. **Validate** — synthetic transaction against the new key (smoke script).
4. **Switch** — promote `<KEY>_NEXT` → `<KEY>`. Deploy.
5. **Retire** — revoke the old key at the provider. Confirm 401 on the old key with a probe.
6. **Audit** — record rotation in `docs/speaking/changelog.md`.

## Provider-specific notes

### Anthropic / OpenAI / ElevenLabs

- Use organization-scoped keys, never workspace-scoped.
- Set per-key spend cap at the provider to bound blast radius.
- Probe path: a 1-token completion / single-character TTS.

### LiveKit Cloud

- API key + secret are paired; rotate as a unit.
- Webhook signing secret can be rotated independently; LiveKit supports a 24-hour grace window where the new secret is also accepted — schedule retirement after that.
- Probe path: mint a learner JWT and connect to a synthetic room.

### AWS S3 (egress)

- Prefer IAM role assumption over long-lived access keys in production. The env-key path is a fallback.
- Rotate the role's trust policy alongside the key.
- Probe path: signed `PutObject` to a probe key, then `DeleteObject`.

## Emergency rotation (compromise suspected)

1. Revoke the old key at the provider **first** (accept transient errors).
2. Issue new key.
3. Deploy direct (no dual-deploy phase).
4. Open an incident ticket; refer to `docs/speaking/incident-runbook.md` Sev1.
5. Audit `AuditEvent` rows for the compromise window.
