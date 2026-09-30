# Speaking live voice agent (AI patient)

Record of the September 2026 rollout and testing. Written 2026-09-30 from the code, GitHub Actions logs and the
run artifacts of the 25-26 Sep production E2E runs. **Items marked "pending" have not been verified in production.**

## Providers

| | OpenAI GPT-Live (production default) | Gemini Live |
| --- | --- | --- |
| Model | `gpt-live-1`, WebRTC via `POST /v1/live/sessions` | `models/gemini-3.8-live`, browser WebSocket with an ephemeral token |
| Selected by | `LIVEVOICE__PRIMARYPROVIDER=openai` (code default, `appsettings.json`, compose) | `?voiceProvider=gemini` on the practice/exam page. Any learner can use it if the provider is configured and verified; there is no role or flag gate |
| Billing (published) | $0.05 per minute of session, billed per second | Tokens: $0.75/M text in, $3.00/M audio in, $4.50/M text out, $12.00/M audio out |
| Measured usage | provider `session.closed` -> `usage.seconds`: 286 s per 5-minute role play, 298 s per exam card | `usageMetadata` per turn |

Cost caveat: only the seconds and token counts are measured. OpenAI $0.238 (286 s) and $0.497 (596 s, two cards) are
billed seconds x $0.05/min. Gemini about $0.27-0.35 per 5 minutes assumes Google re-bills the whole audio context each turn
(about $0.06 if it does not). None of these is an invoice figure. Claude grading cost has never been measured.

Switch the default: set `LIVEVOICE__PRIMARYPROVIDER` (`openai` | `gemini`) in the VPS env and recreate the API slot.
Keys: `LIVEVOICE__OPENAI*`, `LIVEVOICE__GEMINI*`. Values are never logged.

## Behaviour and limits

- One provider session per card. Its instructions are built from that card only (`LiveVoiceService.BuildInstructions`);
  hidden roleplayer information is protected by the prompt alone.
- The saved transcript is built in the browser (`hooks/useSpeakingRealtimeVoice.ts`) and graded verbatim.
  GPT-Live transcribes the candidate a beat behind real time. PR #257 ordered fragments by the provider's `start_ms`,
  but GPT-Live's own timeline can place a sentence's last word at or after the patient's backchannel, so the saved
  transcript of the 26 Sep two-card mock still read "...How can I help you" / Patient "Uh." / Candidate "today".
  `isLateFragment` additionally rejoins a mid-sentence continuation (lowercase or punctuation start) that follows a
  short (<= 3 words) backchannel of the other speaker. Whole sentences keep the provider's order.
  **Pending: not yet verified in production** (run the E2E and read `noSplitSentences`).
- Prompt rules: candidate-first; TEACH-BACK (never fill the gap from the card; RULE_18 is the separate honest-response
  rule); and, added with this change and **pending verification**, never attribute unsaid treatments, tests or
  referrals to the doctor (2 of 5 post-#257 teach-back replies did).
- No audio is stored ("Recording unavailable" on the transcript page); grading sees the transcript only.
- No reconnect and no cross-provider fallback: a dropped session ends the patient for that card. Live voice is offered
  only while the primary provider is configured and verified (checked every 5 minutes, primary provider only); otherwise
  the recorder fallback is used, even if the other provider would have worked.
- The app does not record billed seconds or live-voice cost; live voice sits outside the AI budget caps and the OpenAI
  offer has no server-side duration cap (the client timers bound a session).
- Grader: default route `claude-sonnet-5`; the gateway forces effort `max`, adaptive thinking and `max_tokens` 128000 for
  `speaking.grade` (the production route row was not re-read). A full 5-minute role play took about 8-9 minutes to grade
  in the 26 Sep runs (PR #259 quotes 11-12). Deploys stop the AI worker with a 90 s grace, so an in-flight grade is
  requeued and restarts from zero (derived from the code; not observed in production).

## Production E2E

Workflow **Speaking live voice E2E (production)** (`.github/workflows/speaking-live-voice-prod-e2e.yml`), harness
`scripts/qa/speaking-live-voice-browser-e2e.mjs` with in-page probes in `scripts/qa/live-voice-browser-probes.mjs`.
Real Chromium, fake microphone playing a scripted candidate once (6 s lead-in, no loop), the QA learner, the real provider.
Runs queue one at a time (concurrency group), never dispatch during a deploy.

- Inputs: `mode` practice | exam; `script`; `voice` piper | espeak; `voice_provider` (blank = production default);
  `speak_seconds` (practice; blank = the tape's last speech + 10 s, max 280 because the app auto-ends at 5:00).
  Scripts: `compare` (about 4 minutes of tape with room for the patient to finish, an early teach-back probe before any
  explanation and a late one after it) and `full-lactose` are written for the lactose practice card and are rejected in
  exam mode; `smoke` is the behaviour probe (barge-in, 25/45/60 s silences, teach-back before explaining); `short` and
  `generic` are minimal.
- Artifacts (`live-voice-e2e`): `metrics.json` (usage, latency, barge-in / talk-over / silences, stability incl.
  WebSocket close codes and RTC states, checks), `timeline-events.json` (timed words incl. provider `start_ms`/`end_ms`),
  `saved-transcript-<session>.json` (the transcript API body the grader reads), `card-text.json`,
  `patient-audio-<n>.webm` (what was audible, both providers), screenshots.
- Checks (a red check fails the run): `savedTranscriptsCaptured`, `noSplitSentences` (GPT-Live runs only), `staysInRole`,
  `noBrowserErrors`, `bargeInPatientStops`, `noPatientTalkOver`, `survivesSilences`. Gemini patient speech is measured
  from playback timing, so its barge-in/talk-over numbers are not comparable with GPT-Live's pass/fail thresholds
  without care. `metrics.leakCheck` (exam) is a bag-of-words suspect list, not a check: read both transcripts.
  Teach-back is judged by reading the saved transcript, not by the harness.

## Known open items

- No E2E has run since 26 Sep 19:27 UTC: PR #265 (AI Assistant 401 loop, grading retry) and every later change are unverified.
- The 26 Sep two-card mock ended red; only 1 of 4 exam runs that completed both cards was ever graded (bugs fixed by #259/#264).
- Gemini vs OpenAI: one matched pair (n=1 each); no complete graded Gemini run. Claude grading cost unmeasured.
- Live-voice sessions store no audio; the results copy still says "We received your recording".
- No reconnect or provider fallback; `?voiceProvider=gemini` is not restricted; hidden information is prompt-only.
- Both exam cards showed the same "Role-play card No." label on 26 Sep.
- The deploy workflow asserts no build identity; only the local `scripts/ship/watch-deploy.ps1` does (`LIVE_SHA_OK`).
- Scheduled Speaking Playwright and accessibility suites: last green 7-8 Sep; the failures on 28 Sep were real
  (auth refresh 400, target-size); the 29 Sep runs were Actions billing blocks.
