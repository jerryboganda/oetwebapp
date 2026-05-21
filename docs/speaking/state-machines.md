# Speaking Module — State Machines

## `SpeakingSession`

```mermaid
stateDiagram-v2
  [*] --> WarmUp
  WarmUp --> Prep: finish-warmup
  Prep --> Active: start-roleplay
  Active --> Finished: end (manual / time-up / disconnect)
  Active --> Cancelled: cancel
  Prep --> Cancelled: cancel
  WarmUp --> Cancelled: cancel
  Finished --> [*]
  Cancelled --> [*]
  Active --> Expired: idle timeout
  Expired --> [*]
```

Guarded transitions: any skip (e.g. WarmUp → Active) throws `ApiException("invalid_state_transition")`. Verified by `SpeakingStateMachineGuardsTests`.

## `SpeakingMockSession`

```mermaid
stateDiagram-v2
  [*] --> Pending
  Pending --> Prep1
  Prep1 --> Active1: start-roleplay (RP1)
  Active1 --> Finished1: end (RP1)
  Finished1 --> Bridge: bridge/start
  Bridge --> Prep2: bridge/finish
  Prep2 --> Active2: start-roleplay (RP2)
  Active2 --> Finished2: end (RP2)
  Finished2 --> Aggregated: MockReportAggregationService.AggregateAsync
  Aggregated --> [*]
```

Aggregation averages the two `SpeakingAiAssessment` rows into the combined readiness band.

## `SpeakingLiveRoom`

```mermaid
stateDiagram-v2
  [*] --> Scheduled
  Scheduled --> Provisioning: LiveKit CreateRoom request
  Provisioning --> Active: room_started webhook
  Active --> Ended: room_finished webhook
  Provisioning --> Failed: API error
  Active --> Failed: egress_failed webhook
  Ended --> [*]
  Failed --> [*]
```

Webhook events are append-only into `SpeakingLiveRoom.WebhookEventsJson` with HMAC verification.

## Cancellation rules

- Cancellation allowed from any non-terminal state. Audit row written.
- Recording (if any partial chunks exist) is preserved or purged based on consent state.
