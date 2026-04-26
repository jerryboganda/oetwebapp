# AI Conversation Module — MISSION CRITICAL Specification

**Status:** Production-grade.
**Authority:** Dr. Ahmed Hesham — OET Preparation Platform.
**Rulebook:** `rulebooks/conversation/<profession>/rulebook.v1.json`
**Scoring:** `lib/scoring.ts:conversationProjectedScaled()` / `OetScoring.ConversationProjectedScaled()`
**Feature flag:** `flg-011` (`ai_conversation`).

---

## 1. Purpose

Learners practise the OET Speaking sub-test with a grounded AI partner that:

1. Stays in the role defined by a CMS-authored scenario (patient for role-plays, colleague for handovers).
2. Transcribes speech in real time via a pluggable ASR provider (Azure / Whisper / Deepgram).
3. Replies in character with optional TTS (Azure / ElevenLabs / CosyVoice / ChatTTS / GPT-SoVITS).
4. Evaluates the completed transcript against the 4-criterion OET Speaking rubric, projected to 0–500.
5. Seeds Review Module items for every rule-cited mistake.

Outputs are advisory and do not replace expert grading.

---

## 2. Canonical task types

| Code | Label |
| ---- | ----- |
| `oet-roleplay` | OET Clinical Role Play |
| `oet-handover` | OET Handover |

Configured via `ConversationOptions.EnabledTaskTypes`. IELTS is out of scope.

---

## 3. Mission-critical invariants

### 3.1 Grounded AI only
Every AI call routes through `IAiGatewayService.BuildGroundedPrompt` with `Kind = RuleKind.Conversation`. Ungrounded prompts raise `PromptNotGroundedException`. Feature codes:

| Code | Classification | BYOK |
| ---- | -------------- | ---- |
| `conversation.opening` | non-scoring | allowed |
| `conversation.reply` | non-scoring | allowed |
| `conversation.evaluation` | scoring (platform-only) | **refused** |
| `admin.conversation_draft` | admin-only | platform-only |

### 3.2 Scoring projection
4 rubric criteria × 0–6 → mean → 0–500 scaled via `OetScoring.ConversationProjectedScaled`:

```
0.0 → 0
3.0 → 250
4.2 → 350 (PASS — Grade B, universal Speaking)
5.0 → 417
6.0 → 500
```

4.2/6 = 70% of max; identical anchor semantics to Pronunciation 70/100 ≡ 350/500.

### 3.3 Audio persistence
All learner + AI audio via `IFileStorage`, content-addressed:
```
conversation/audio/{sha[0..2]}/{sha[2..4]}/{sha}.{ext}
```
Retention `ConversationOptions.AudioRetentionDays` (default 30), swept by `ConversationAudioRetentionWorker`.

### 3.4 Review integration
Every annotation of type `error` or `improvement` seeds a `ReviewItem` with `SourceType = "conversation_issue"`.

---

## 4. Architecture

```
Learner browser
  /conversation            landing (catalog + entitlement)
  /conversation/{id}       active session (SignalR + mic)
  /conversation/{id}/results  scaled score + rubric + transcript
        │ SignalR + base64 audio
        ▼
ConversationHub
  StartSession → opening via orchestrator + TTS
  SendAudio    → ASR → grounded reply → TTS
  EndSession   → enqueue evaluation job
        │
        ▼
ConversationAiOrchestrator
  GenerateConversationOpening | GenerateConversationReply | EvaluateConversation
        │
        ▼
IAiGatewayService.BuildGroundedPrompt (rulebook + scoring + scenario + transcript)
```

---

## 5. REST + SignalR API

### Learner
| Verb | Path | Purpose |
| ---- | ---- | ------- |
| GET  | `/v1/conversations/task-types` | Enabled task types |
| GET  | `/v1/conversations/entitlement` | Tier + remaining |
| POST | `/v1/conversations` | Create session |
| GET  | `/v1/conversations/{id}` | Session detail |
| POST | `/v1/conversations/{id}/resume` | Resume an active/preparing session and issue a short-lived resume token |
| POST | `/v1/conversation/sessions/{id}/resume` | Compatibility alias for resume |
| POST | `/v1/conversations/{id}/complete` | Mark complete |
| GET  | `/v1/conversations/{id}/evaluation` | Poll evaluation |
| GET  | `/v1/conversations/{id}/transcript/export?format=txt\|pdf` | Export transcript through `IFileStorage` |
| GET  | `/v1/conversations/history?page&pageSize` | Paged history |
| GET  | `/v1/conversations/media/{sha}.{ext}` | Stream audio |

### SignalR hub `/v1/conversations/hub`
C→S: `StartSession` · `SendAudio(id, base64, mime?)` · `EndSession`
S→C: `ReceiveTranscript` · `ReceiveAIResponse` · `SessionStateChanged` · `SessionShouldEnd` · `ConversationError`


Phase-2 resume/export details:

- `ConversationSessionResumeToken` stores SHA-256 hashes of short-lived resume tokens; raw tokens are returned only to the authenticated learner.
- Session detail and resume responses include persisted turns so the learner UI can hydrate an active transcript before reconnecting to SignalR.
- `StartSession` does not replay the AI opening when persisted turns already exist.
- Transcript exports support `txt` and `pdf`; both are generated from persisted session/turn/evaluation data and written via `IFileStorage` under `conversation/transcripts/{sha[0..2]}/{sha[2..4]}/{sha}.{ext}`.
- ASR requests can enable diarization. Deepgram requests `diarize=true&utterances=true`; Whisper-compatible providers parse segment speaker metadata when present; Azure currently returns a single learner segment fallback when diarization is requested.

### Admin
| Verb | Path |
| ---- | ---- |
| GET  | `/v1/admin/conversation/templates` |
| GET  | `/v1/admin/conversation/templates/{id}` |
| POST | `/v1/admin/conversation/templates` |
| PUT  | `/v1/admin/conversation/templates/{id}` |
| POST | `/v1/admin/conversation/templates/{id}/publish` |
| POST | `/v1/admin/conversation/templates/{id}/archive` |
| GET  | `/v1/admin/conversation/settings` |

### Publish gate
Requires: title + scenario + role + patient context + ≥ 3 objectives + duration > 0 + task type in `{oet-roleplay, oet-handover}`.

---

## 6. Configuration (`ConversationOptions`)

```yaml
Conversation:
  Enabled: true
  AsrProvider: auto      # azure | whisper | deepgram | mock | auto
  TtsProvider: auto      # azure | elevenlabs | cosyvoice | chattts | gptsovits | mock | auto | off
  AudioRetentionDays: 30
  PrepDurationSeconds: 120
  MaxSessionDurationSeconds: 360
  MaxTurnDurationSeconds: 60
  EnabledTaskTypes: [ oet-roleplay, oet-handover ]
  FreeTierSessionsLimit: 3
  FreeTierWindowDays: 7
  ReplyTemperature: 0.6
  EvaluationTemperature: 0.1
  AzureSpeechKey / AzureSpeechRegion / AzureLocale
  WhisperBaseUrl / WhisperApiKey / WhisperModel
  DeepgramApiKey / DeepgramModel / DeepgramLanguage
  AzureTtsDefaultVoice
  ElevenLabsApiKey / ElevenLabsDefaultVoiceId / ElevenLabsModel
  CosyVoiceBaseUrl / CosyVoiceApiKey / CosyVoiceDefaultVoice
  ChatTtsBaseUrl   / ChatTtsApiKey   / ChatTtsDefaultVoice
  GptSoVitsBaseUrl / GptSoVitsApiKey / GptSoVitsDefaultVoice
```

---

## 7. Domain model

| Entity | Purpose |
| ------ | ------- |
| `ConversationSession` | Session header + scenario snapshot + state |
| `ConversationTurn` | Per-turn transcript + audio URL + ASR confidence |
| `ConversationTemplate` | CMS scenario (draft/published/archived) |
| `ConversationEvaluation` | AI-graded evaluation (4 criteria + meta) |
| `ConversationTurnAnnotation` | Per-turn finding (strength/error/improvement) |
| `ConversationSessionResumeToken` | Short-lived hashed resume token for reconnect/session-resume flow |

---

## 8. Common gotchas

- **Never** call the gateway with a non-`Conversation` Kind from conversation code.
- **Never** persist audio via raw `File.*` — always `IConversationAudioService` / `IFileStorage`.
- **Never** compare a rubric mean to `>= 4.2` inline — use `ConversationProjectedScaled`.
- **Never** serialise `AudioUrl` without the `/v1/conversations/media/` prefix.
