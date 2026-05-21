# Speaking Module — LiveKit Integration

## Gateways

- `LiveKitGatewayStub` — default in local/dev. Synthetic SIDs, no real connections.
- `LiveKitCloudGateway` — real LiveKit Cloud. Registered in `Program.cs` when `LiveKitOptions.IsEnabled = true`.

## Room lifecycle

`Scheduled → Provisioning → Active → Ended (or Failed)`. Persisted on `SpeakingLiveRoom.State`. Webhook events appended to `WebhookEventsJson`.

## JWT capability scoping

- **Learner**: `roomJoin + canPublish (own tracks) + canSubscribe`
- **Tutor**: `roomJoin + canPublish + canSubscribe + roomAdmin`

Short-lived TTL (session duration + ~1h grace). Issued through `/v1/speaking/live-rooms/{id}/token`.

## Egress (recording)

- Track-composite egress, AAC + h264.
- Destination: S3 bucket from `LiveKitOptions.EgressBucket`.
- On `egress_ended` webhook, persist into `SpeakingRecording` (Kind = Mixed, Source = LiveTutor).
- Consent version stamped from `SpeakingLiveRoom.RecordingConsentVersion`.

## Webhook security

Endpoint `POST /v1/speaking/live-rooms/webhooks/livekit`:
- HMAC verification with constant-time compare against `LIVEKIT__WEBHOOKSIGNINGSECRET`.
- Source-IP allow-list (LiveKit Cloud documented egress IPs).
- Append-only event log per room.

## Scaling notes

- One LiveKit room per booking; teardown at `ScheduledStartAt + LIVEKIT__DEFAULTMAXDURATIONSECONDS`.
- Track-composite egress is single-region; S3 bucket region matches LiveKit region for latency.
- Rooms above the per-project SDP / participant cap require LiveKit Cloud Enterprise.

## Ops

See `docs/speaking-module-runbook.md` for incident response. Key rotation procedure: `docs/security/speaking/key-rotation.md`.
