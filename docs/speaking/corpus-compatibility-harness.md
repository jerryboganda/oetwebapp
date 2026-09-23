# Speaking corpus compatibility harness ($0)

`POST /v1/admin/speaking/corpus-compatibility` checks that every published
role-play card works on the Speaking path (owner PDF §10B/§10C). The path is:
load → preparation → candidate-first conversation → AI patient → timer →
submit → transcript → grading/result. The output names each failing card by
id, number, profession and stage. It never falls back to mock data.

**It makes zero provider calls and persists nothing.**

- No speech-to-text, no Jev/TypeSafe, no model or grading provider, and no
  live-voice provider is called.
- Each card runs in its own DI scope and in a database transaction that is
  always rolled back. This covers the synthetic learner, its credits, the
  session, the transcript, the credit hold and the assessment.
- No audio blob is written.
- The grader reply is a fixed canned JSON. It goes through the real
  parse/clamp/scale code. Every report says `"mode": "controlled-no-provider"`
  so nobody mistakes it for real-voice evidence.
- Nothing is written to the audit table. The API log records who ran the
  harness and the pass count.

## Call it on production

This needs an admin with `content:read` and `content:write`.

```bash
API=https://api.oetwithdrhesham.co.uk

# 1. Sign in (the admin account; complete MFA first if the account requires it)
TOKEN=$(curl -s -X POST "$API/v1/auth/sign-in" \
  -H 'Content-Type: application/json' \
  -d '{"email":"<admin email>","password":"<admin password>","rememberMe":false}' \
  | jq -r .accessToken)

# 2. First page: cards 0-49 plus the §10C per-profession section
curl -s -X POST "$API/v1/admin/speaking/corpus-compatibility?offset=0&limit=50" \
  -H "Authorization: Bearer $TOKEN" > corpus-0.json

# 3. Keep paging while nextOffset is not null
curl -s -X POST "$API/v1/admin/speaking/corpus-compatibility?offset=50&limit=50" \
  -H "Authorization: Bearer $TOKEN" > corpus-50.json
```

A full pass of about 417 cards is 9 pages at the default limit of 50. The
maximum limit is 100.

The endpoint uses the `PerUserWrite` rate limit, so page one request at a time.
Useful filters:

- `profession=medicine` checks one profession only.
- `cardId=<id>` re-checks one card, by card id or content-item id.

Quick failure list across pages:

```bash
jq -r '.cards[] | select(.passed|not) | [.professionId, .displayCardNumber, .cardId, .failedStage, .error] | @tsv' corpus-*.json
```

## Response

```jsonc
{
  "mode": "controlled-no-provider",
  "generatedAt": "…",
  "offset": 0, "limit": 50, "totalPublishedCards": 417, "nextOffset": 50,
  "freeSamplesEnabled": true,              // free_samples_enabled feature flag
  "totals": { "cards": 50, "passed": 49, "failed": 1 },   // this page
  "professions": [                          // first page (offset=0) only
    { "professionId": "medicine", "publishedCards": 40, "candidateLoadableCards": 40,
      "freeSampleCardId": "…", "freeSampleCardPassed": true, "state": "ok",
      "writingFreeSampleScenarioId": "…", "writingState": "ok" }
  ],
  "cards": [
    { "cardId": "…", "displayCardNumber": 12, "professionId": "medicine", "title": "…",
      "passed": false, "failedStage": "live_voice_readiness", "error": "…",
      "stages": [ { "name": "card_published", "ok": true, "detail": "…" } ] }
  ]
}
```

A profession `state` is one of:

- `ok`: the profession's free card resolves and passes every stage.
- `controlled_unavailable`: no eligible card, so learners see the controlled
  unavailable state.
- `free_card_failing`: a card is offered as the free sample but fails a stage.
  Fix it before go-live.

`writingState` is `ok` or `controlled_unavailable`. It is based on the Writing
free-sample pick, which is a published, candidate-loadable scenario.

## Stages

Every stage is checked separately. `failedStage` is the first one that fails.

| Stage | What it proves |
| --- | --- |
| `card_published` | The card is Published. Its linked content item exists, is Published and is a `speaking` item. The card has a profession. |
| `learner_projection` | The learner card (`SpeakingSessionService.ProjectLearnerCard`) has role/profession, setting, background and non-empty tasks. It has no emotion, goal or topic keys. |
| `live_voice_readiness` | There is an interlocutor script that does not need owner input: an authored script, or a still-current Jev-validated projection. Readiness is resolved read-only, so the harness never generates a projection and never calls Jev. |
| `ai_patient_instructions` | `LiveVoiceService.BuildInstructions` builds for the card. The result has the candidate-first rule and the `FOR CONTEXT ONLY` label, and no candidate task text appears outside that block. |
| `session_state_machine` | Runs the real `SpeakingSessionService` for a synthetic learner of the card's profession, with 4 speaking credits granted through the admin ledger. The steps are: create (`ai_self_practice`), consent, warm-up, prep (timer running), role-play (timer running), recorder-fallback recording row, real transcription queue row, 8 scripted candidate-first turns promoted by `SpeakingTranscriptionPipeline.PromoteLatestAsync`, end, submit. The detail says which step failed. |
| `grading_contract` | Runs the real canonical assessment and classic assessor with a canned model reply. The checks: exactly one canned call, a scaled score between 0 and 500, all 9 criteria present, and `assessmentState=completed` for the results endpoint. |

## Limits

- This does not prove real-voice quality. Real STT and grading are covered by
  owner/QA acceptance on the recorder fallback, at normal product cost.
- `live_voice_readiness` is strict. A card without an approved interlocutor
  script fails here even though the recorder fallback, which needs no script,
  still works for learners.
- Running the harness is load on the production database: one short
  transaction per card, one after another. Run it outside peak hours.
