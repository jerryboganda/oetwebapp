# Speaking Module — Analytics Events

Typed catalog: `lib/analytics/speaking-events.ts`. Use `trackSpeaking('name', { ... })`.

## Catalog

| Event | Properties | Fired when |
|-------|------------|------------|
| `module_entry` | `from` | `/speaking` mounts |
| `profession_set` | `professionId` | learner saves profession on the gate |
| `warmup_started` | `sessionId` | `WarmUp` state entered |
| `warmup_finished` | `sessionId`, `durationSeconds` | learner clicks "Start preparation" |
| `prep_started` | `sessionId`, `cardId` | `Prep` state entered |
| `roleplay_started` | `sessionId`, `cardId` | `Active` state entered |
| `roleplay_time_nearly_up` | `sessionId` | SignalR `TimeNearlyUp` event received |
| `roleplay_ended` | `sessionId`, `durationSeconds`, `reason` | `Finished` state entered (`reason` = manual_end \| time_up \| disconnect) |
| `ai_assessment_viewed` | `sessionId`, `estimatedBand` | results page renders AI assessment |
| `tutor_assessment_viewed` | `sessionId` | results page renders tutor assessment |
| `drill_started` | `drillId`, `criterion` | learner begins a drill |
| `drill_scored` | `drillId`, `score` | drill AI feedback returns |
| `pathway_viewed` | — | `/speaking/pathway` mounts |
| `recording_deleted` | `recordingId` | learner confirms delete in `/speaking/recordings` |
| `mock_started` | `mockSetId` | learner starts a mock set |
| `mock_bridge_viewed` | `mockSetId` | bridge page mounts |
| `mock_aggregated` | `mockSetId`, `estimatedBand` | aggregated results render |
| `live_room_joined` | `liveRoomId`, `role` | LiveKit connect confirmed (role = learner \| tutor) |
| `live_room_ended` | `liveRoomId`, `durationSeconds`, `reason` | LiveKit disconnect |
| `cue_raised` | `liveRoomId`, `cueId` | tutor clicks a cue button |
| `consent_accepted` | `consentType`, `consentVersion` | consent banner confirmed |
| `consent_revoked` | `consentType` | learner revokes consent |
| `card_ai_drafted` | `cardId`, `profession` | admin AI-draft endpoint returns |
| `card_published` | `cardId`, `profession` | admin publishes a card |

## Downstream consumers

- **Funnel dashboard** (`ops/dashboards/speaking-funnel.json` — pending) — entry → warmup_finished → roleplay_ended → assessment_viewed.
- **Quality dashboard** — AI vs tutor delta histograms by criterion.
- **LiveKit health** — connect rate, disconnect reasons, cue volume.

## TODOs

- Wire `roleplay_ended.reason` once Agent B's SignalR end-reason field lands.
- Resolve analytics destination (PostHog assumed).
