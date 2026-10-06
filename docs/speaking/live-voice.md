# Speaking live voice agent (AI patient)

> **Status 1 Oct 2026 (pre-launch hardening).** A second change set after the 30 Sep close-out, written from the 1 Oct audit of
> the production runs and reconciled with the code of branch `feat/speaking-final-verification-2026-10-01`. What changed, one
> line each (the linked sections hold the detail):
>
> - **QA provider pin:** `?voiceProvider=` only asks. The server honours it for an account with an enabled
>   `speaking_live_voice_pin:<learner user id>` feature flag and ignores it for everyone else, and the browser trusts only the
>   server's `pinned` ([QA provider pin](#qa-provider-pin)).
> - **One transcript clock per role-play:** saved segment times are milliseconds since the first provider session went live, so
>   a recovery no longer merges the conversation after it into earlier segments ([Transcript time base](#transcript-time-base)).
> - **Refresh-safe transcript:** a copy of the conversation in the tab's `sessionStorage` is restored when the same card
>   reloads, and turn ids no longer repeat across page instances ([Refresh behaviour](#refresh-behaviour)).
> - **Transcript details:** a silence of more than 10 s starts a new segment, a lone-space delta no longer fuses two words and a
>   Gemini role-play waits 2 s before it stops ([Pause rule](#pause-rule), [Whitespace deltas](#whitespace-deltas),
>   [Stopping a Gemini role-play](#stopping-a-gemini-role-play)).
> - **Honest results wording:** `GET /v1/speaking/sessions/{id}/results` returns `inputKind`; the results pages, two backend
>   failure texts and the grader's input stop implying an audio recording for a live conversation
>   ([Results wording by input kind](#results-wording-by-input-kind)), and the exam results page shows the band by label, an
>   advisory note, per-card links and an honest not-completed state ([Exam results page](#exam-results-page)).
> - **Live microphone clips (implemented after the runs above; not production-verified):** current consented live sessions
>   store short clips from the learner microphone during detected speech, linked to verified transcript evidence. The app
>   does not make a full-session recording or directly record provider playback; browser echo cancellation cannot guarantee
>   that provider or background audio is excluded ([Consent and disclosure](#consent-and-disclosure)).
> - **Microphone wording** names the live control and never a recording ([Microphone error wording](#microphone-error-wording)).
> - **Credits:** the Card A hold is taken first in `finish-intro` (a refused hold leaves the exam in Intro), `ai_exam` can no
>   longer be created through `POST /v1/speaking/sessions`, and the reservation insert survives a cancelled request
>   ([state-machines.md](state-machines.md#credits-ai-exam-and-practice-card)).
> - **History:** a Speaking mock is one "Full Speaking Mock" row with a result label, and session-bound Speaking attempts leave
>   Past Evidence ([api-surface.md](api-surface.md#history-notes)).
> - **Production E2E:** faults combine with `fail_primary`; new `fault_reload_at_s`, `verify_credits` and `grade_retry` inputs;
>   saved-transcript, credit, History, pin and wording checks; a run matrix ([Production E2E](#production-e2e)).
>
> **Verified in production on 1 Oct 2026 (build 679e29cdc) by the full run matrix** ([Run matrix](#run-matrix)): credits (exactly
> 4 for a mock, once; 2 for a practice card; nothing charged twice by a repeated grade), the History row, the honest results
> wording and `inputKind`, the one transcript clock (no split sentence on any run), the refresh-safe transcript (a refresh in the
> middle of a card resumed by itself in 5.7 s) and Gemini failover with mid-session recovery (a dropped link restored in
> 0.0-0.3 s). **Not verified in production:** the QA provider pin (its flag was never created), the teach-back rule against unsaid
> treatments, the microphone error wording, an OpenAI restore after the time base change, the OpenAI hang-up at the hard stop and a
> real provider outage. The matrix found three Gemini limits ([Known open items](#known-open-items)); the Gemini start-order one
> was fixed in code on 2 Oct 2026 and has not been re-run on production. The earlier 1 Oct runs had verified the late-fragment
> rule on clean OpenAI runs ([Behaviour and limits](#behaviour-and-limits)) and exposed the recovery transcript defect that the
> time base change addresses.

Record of the September 2026 rollout and testing. Written 2026-09-30 from the code, GitHub Actions logs and the
run artifacts of the 25-26 Sep production E2E runs; extended the same day with the close-out of the 30 Sep incident
(OpenAI session creation answered 429, so learners saw "could not start this conversation" and there was no
fallback). **Items marked "pending" have not been verified in production.** Everything under "Provider failover",
"Provider health", "Hard duration cap", "Consent and disclosure" and "Card labels" is new in that close-out and is
pending until the production E2E below has been run against it.

Reconciled on 2026-09-30 with the final code of the close-out branch (`feat/speaking-resilience-2026-09-30`), which is the
source of truth where this file and the code differ: `LiveVoiceOptions`, `LiveVoiceService`,
`LiveVoiceProviderProbeState`, `SpeakingExamAutoAdvanceWorker` and `SpeakingSessionService`. Parts of the close-out have since
run in production (30 Sep - 1 Oct; see the measurements below and [Known open items](#known-open-items) for what is still
unverified).

## Providers

| | OpenAI GPT-Live (production default) | Gemini Live |
| --- | --- | --- |
| Model | `gpt-live-1`, WebRTC via `POST /v1/live/sessions` | `models/gemini-3.8-live`, browser WebSocket with an ephemeral token |
| First choice | `LIVEVOICE__PRIMARYPROVIDER=openai` (code default, `appsettings.json`, compose): the first candidate whenever it is configured, verified and its circuit is not open; Gemini is then the failover | `LIVEVOICE__PRIMARYPROVIDER=gemini`; or, for a flagged QA learner only, `?voiceProvider=gemini` on the practice/exam page, which **pins** the run to that provider (no failover, circuit bypassed). The page value only asks: the server honours it only for an account holding an enabled `speaking_live_voice_pin:<learner user id>` feature flag ([QA provider pin](#qa-provider-pin)); for everyone else it is ignored |
| Billing (published) | $0.05 per minute of session, billed per second | Tokens: $0.75/M text in, $3.00/M audio in, $4.50/M text out, $12.00/M audio out |
| Measured usage | provider `session.closed` -> `usage.seconds`: 286 s per 5-minute role play, 298 s per exam card | `usageMetadata` per turn |

Cost caveat: only the seconds and token counts are measured. OpenAI $0.238 (286 s) and $0.497 (596 s, two cards) are
billed seconds x $0.05/min. Gemini about $0.27-0.35 per 5 minutes assumes Google re-bills the whole audio context each turn
(about $0.06 if it does not). None of these is an invoice figure. Claude grading cost has never been measured.

Switch the default: set `LIVEVOICE__PRIMARYPROVIDER` (`openai` | `gemini`; a blank or unknown value orders OpenAI first)
in the VPS env and recreate the API slot. Keys: `LIVEVOICE__OPENAI*`, `LIVEVOICE__GEMINI*`. Values are never logged. Every
key is listed in [../env/speaking.md](../env/speaking.md).

Default history: `openai` has been the code/compose default since 22 Sep 2026 (`74fdadd80`); on 26 Sep the VPS env was
switched from Gemini to OpenAI by hand (backup `.env.production.bak-2026-09-26-primary-openai`); both API slots ran
`openai` on 30 Sep 04:08 UTC.

### Owner decision 2026-10-05: OpenAI primary, Gemini fallback

The owner decided that **OpenAI GPT-Live stays the primary** live-patient provider and Gemini the fallback (this replaces the
earlier request to try Gemini first). That is already the code, compose and `appsettings.json` default
(`LIVEVOICE__PRIMARYPROVIDER=openai`). If the production env still carries the temporary `LIVEVOICE__PRIMARYPROVIDER=gemini`
from that earlier request, it must be removed (or set to `openai`) **in place** in `/opt/oetwebapp/.env.production` by the
owner or ops after a `cp -p` backup (agents do not edit `.env*`; never append a second line, never use an alias), then the
inactive API slot recreated and `ACTIVE_SLOT` read back. Admin > Voice design > Live patient voices shows the server's live
order and warns when Gemini is first; `GET /v1/admin/ai/live-voice/health` lists `candidateOrder`.

**Voice pool.** The OpenAI voices are quartz, willow, ripple and vesper (female younger / female older / male younger / male
older). Gemini's Leda, Kore, Orus and Charon stay configured as the fallback voices. The main voice-quality decision is on the
OpenAI voices. **Listening check:** Admin > Voice design > Live patient voices has one Play button per voice (a ~20 s GPT-Live
session in the admin's browser saying a short patient sentence; nothing is recorded). A preview is an admin-audited
(`LiveVoicePreviewStarted`) live-voice provider session: like learners' live sessions it writes no `AiUsageRecord` and sits
outside the AI budget caps, and it never feeds the provider circuit breaker. Owner approval of the voices: _pending the
owner's listening check_ (record the date here once given).

## Behaviour and limits

- One card is normally served by one provider to the end. Failover happens *before* the link is live
  ([Provider failover](#provider-failover)); a link that dies or goes silent *after* it was live is restored on a new
  provider session ([Mid-session recovery](#mid-session-recovery)). So a card can mint more than one provider session (a
  leg that failed after its create call, a restore, a reload); at most
  `MaxProviderSessionsPerRolePlay` (3) are allowed ([Hard duration cap](#hard-duration-cap)). Instructions are built from
  that card only (`LiveVoiceService.BuildInstructions`); hidden roleplayer information is protected by the prompt alone.
- The saved transcript is built in the browser (`hooks/useSpeakingRealtimeVoice.ts`) and graded verbatim.
  GPT-Live transcribes the candidate a beat behind real time. PR #257 ordered fragments by the provider's `start_ms`,
  but GPT-Live's own timeline can place a sentence's last word at or after the patient's backchannel, so the saved
  transcript of the 26 Sep two-card mock still read "...How can I help you" / Patient "Uh." / Candidate "today".
  `isLateFragment` additionally rejoins a mid-sentence continuation (lowercase or punctuation start) that follows a
  short (<= 3 words) backchannel of the other speaker. Whole sentences keep the provider's order.
  **Verified in production on 1 Oct 2026** for GPT-Live runs without a recovery (run labels such as E1 or F1 name the 1 Oct
  production run artifacts): the E1 exam (both cards) and the O2, O4, O5b and O6 runs showed 0 split sentences
  (`noSplitSentences`) and a clean order. Replaying their saved provider events shows the rule joining a late tail 6 times
  (once on E1 Card A, three times on O4, twice on O6), every time a plausible continuation. `noSplitSentences` only matches a
  lowercase continuation sandwiched by a backchannel of at most 3 words, so it could not see the split a recovery caused before
  the 1 Oct time base change ([Transcript time base](#transcript-time-base)); the [transcript checks](#transcript-checks) of the
  production E2E can. How the saved times, pauses, whitespace and a reload are handled: [Saved transcript](#saved-transcript).
- Prompt rules: candidate-first and an explicit-invitation opening ([Opening, identity and voices](#opening-identity-and-voices));
  TEACH-BACK (never fill the gap from the card; RULE_18 is the separate honest-response
  rule); and, added with this change and **pending verification**, never attribute unsaid treatments, tests or
  referrals to the doctor (2 of 5 post-#257 teach-back replies did).
- The earlier production run matrix exercised transcript-only live voice and correctly reported that no audio was stored for
  that build. Current code stores consented short clips captured from the learner's microphone during local speech activity;
  it does not make a full-session recording or directly record provider playback. Browser echo cancellation is enabled but
  cannot guarantee isolation, so provider or background audio may still be picked up by the microphone. Learner result
  wording identifies the transcript as the graded submission, hides the full-session recorder player, and exposes only
  verified source-linked microphone clips in the v1.1 report. This newer clip path is implemented but is not
  production-verified ([Results wording by input kind](#results-wording-by-input-kind)).
  A departing card snapshots its transcript and audio-link collection before awaiting provider close. Its pending
  microphone uploads remain attached to that card even if the next card starts first; completing an old save must
  neither persist the next card's words nor clear its provider state. Recording stops when card shutdown begins.
- A connection that drops mid-conversation, or a patient that stays silent, is restored automatically up to twice per card
  ([Mid-session recovery](#mid-session-recovery)); after that (or for a run the server pinned, QA accounts only) the error is
  shown as before. A provider that cannot *start* is replaced by the other one automatically
  ([Provider failover](#provider-failover)). The recorder fallback is used
  only while **no** provider is usable (`liveVoiceAvailable` = at least one candidate; it was primary-only before). The
  flag is computed whenever a session or exam DTO is read, and each page fixes the mode of a card when the card starts
  (see the client fail-open rules under [Hard duration cap](#hard-duration-cap)).
- The app does not record billed seconds or live-voice cost; live voice sits outside the AI budget caps. Duration is
  bounded by the [hard duration cap](#hard-duration-cap).
- Grader: `speaking.grade` tries the Claude Max subscription sidecar first (`writing-claude-sub`, Opus 5.5, effort `high`,
  under one wall-clock budget of `Speaking:Grading:PinnedTimeoutSeconds`, 900 s) and falls back to the default route
  (`claude-sonnet-5`; the gateway forces effort `max`, adaptive thinking and `max_tokens` 128000 for `speaking.grade`
  there; the production route row was not re-read): see
  [ai-providers.md](ai-providers.md#speaking-grading-chain-owner-directive-2026-09-30). A full 5-minute role play took
  about 8-9 minutes to grade on the API route in the 26 Sep runs (PR #259 quotes 11-12); on the sidecar (1 Oct 2026, from the
  admin operation rows of RUN 2, 4 and 5, grade request to completed result) a card took 51 to 114 s, mostly 61-81 s, and every
  card of every run was graded by `writing-claude-sub` / `claude-opus-5-5`. Deploys stop the AI worker with a 90 s grace, so an in-flight grade is requeued and restarts from zero
  (derived from the code; not observed in production).

## Opening, identity and voices

Owner spec 4 Oct 2026 (live patient; prompt and provider configuration only, no change to the transport or the recorder):

- **The patient waits to be invited.** It never speaks first. A greeting, an introduction, a name exchange or a pause is **not**
  an invitation to tell the story: a bare "good morning" gets a brief greeting back (a few words) and the patient waits; "how may
  I address you?" gets the name in a few words and the patient waits. The opening response comes only after an explicit
  invitation to say why they are here ("what brings you in today?", "how can I help you?", "what seems to be the problem?",
  "tell me what has been happening"). A narrow question gets a narrow answer; a clinical question asked before any
  introduction is answered naturally and briefly, never with a comment on the order of the conversation. A return-visit card
  (`Second Visit / Follow-up`, or a script that allows a second visit) adds one sentence confirming why they came back,
  once the candidate has said what they want to go through. An INTERRUPTION POLICY paragraph asks the patient to wait four to
  five seconds of silence before saying anything, and then only a few words. GPT-Live owns its turn detection and has **no VAD
  settings** (OpenAI documents none), so these behaviours are controlled in the prompt; on Gemini the setup also asks for a
  longer wait for the end of the candidate's turn (`realtimeInputConfig.automaticActivityDetection`:
  `endOfSpeechSensitivity` LOW, `silenceDurationMs` 1200; the start of speech keeps its default so a real interruption still
  stops the patient). A restored session answers the candidate's last line first only when it asked a question or invited the
  patient to speak.
- **A real, stable name** (`LiveVoicePatientIdentityResolver`). A card name is used as written; a blank or missing one gets a
  plain fixed name (James or Jack, Anne or Sarah) chosen from the card id and gender, so the same card is the same person on
  every provider, after a reconnect and after a failover. A speaker who is not the patient (a parent, carer, relative,
  nurse...) gets their own name and the card name becomes the patient's. A blank `PatientName` is no longer stored as an empty
  string on the create path. "Your name is X; a question about your name never starts your story" is in the instructions.
- **Voices.** A two-by-two table per provider, gender by age band (under 45 / 45 and over), chosen from the card: OpenAI
  GPT-Live `session.audio.output.voice` (fixed at session start; defaults quartz, willow, ripple, vesper: Australian, Irish,
  Australian, British) and Gemini `generationConfig.speechConfig` (Leda, Kore, Orus, Charon; the ephemeral token may ignore
  it: a Google forum report says so for the 3.1 live preview). Any cell can be overridden (`LIVEVOICE__OPENAIVOICEMALEOLDER`
  and the like); a blank cell means the provider's own voice. The picks are **not verified by ear**: the owner's listening check
  confirms or changes them.
- **Silence fallback.** The browser's Gemini end-of-audio nudge now comes 5 s after an unanswered sentence (it was 8 s); the
  restore still follows at 20 s.
- **Checked by** the `opening-control` script of the production E2E (`checks.openingControl`): the patient never speaks first,
  says at most about twenty words before the invitation (a greeting back, a name, a thank you) and answers the invitation.

## Provider failover

Failover is executed by the browser and decided by the server, because the OpenAI leg needs a browser-made SDP offer
and the Gemini leg needs the browser to open the WebSocket: the server cannot finish a cross-protocol failover alone.

**Contract** (`lib/api/speaking-live-voice.ts`; all additions are optional, so an old client or an old server keeps working):

- `GET /v1/speaking/realtime/sessions/{id}/preflight[?provider=]` returns its usual fields plus `candidates` (the
  providers to try, in order: the configured primary first, then the other, each only if it is configured,
  catalog-verified and its circuit is not open; `provider` is always `candidates[0]`) and `pinned` (true only when the
  server honoured a QA pin: a provider was requested **and** the signed-in learner holds the enabled flag, see
  [QA provider pin](#qa-provider-pin). There is then one candidate, no failover and the circuit is bypassed. A request from
  any other account is ignored: the normal automatic order (primary first, circuit respected) and `pinned: false`, with
  nothing in the response saying it was ignored). No `candidates` (old server) means one attempt with `provider`.
  `disclosure` names every candidate ([Consent and disclosure](#consent-and-disclosure)).
- `POST .../openai/offer` and `POST .../gemini/token` keep their shapes and add `hardStopAt` (ISO time).
- Whatever a provider answers (401, 429, 400, 5xx, a timeout, an unreadable 200) the browser only sees a generic
  **HTTP 503**; the server has recorded the real class ([Provider health](#provider-health)). A 503 is the signal to try
  the next candidate. Codes and flags: `live_voice_provider_unavailable`, `_timeout` and `_invalid_response` (a create
  call failed) and `_not_configured` carry `retryable: false`; `_unverified` (the model has not passed the catalog probe
  yet) and the preflight's own `_unavailable` (no candidate right now) carry `retryable: true`. The client keys off the
  status class, not the flag.
- Any 4xx other than 408 is a definite answer about this session and is never failed over.

**Client loop** (`useSpeakingRealtimeVoice`):

1. Preflight gives the plan (`planProviders`): the server's candidates, de-duplicated, or `[provider]` when there are none,
   or `[provider]` when the server pinned the run (`preflight.pinned`, QA accounts only). The browser trusts only the server's
   `pinned`, never the page's `?voiceProvider=`, which only asks for a pin and never shrinks the plan
   ([QA provider pin](#qa-provider-pin)).
2. `start()` is single-flight and asks for the microphone **once**; the stream and level meter are reused by every attempt.
   `stop()` and unmount release a microphone that was opened while `AudioContext.resume()` is still pending (it can stay
   pending on WebKit and WebViews), so the microphone can never be left open by a cancelled start.
3. Attempts are strictly sequential (the API admits one live-voice request per user; a parallel call would be answered 429).
   Each attempt creates the provider session, then waits for the link to go **live** (OpenAI: data channel open, peer
   connected or `session.started`; Gemini: `setupComplete`) within a 15 s deadline. Any failure before that ends the attempt.
4. Between attempts the failed provider's transport is torn down (handlers detached **before** `close()`, so a late
   WebSocket close cannot flip the next attempt to an error). The learner sees nothing change: the label stays
   "Connecting to the AI patient…".
5. The provider and its session id are committed **together, only once the link is live**. A leg that never connected
   leaves no id behind, so `stop()` can never save a transcript under the wrong provider or with an empty transcript.
6. The whole chain is cancelled by `stop()`, unmount or a session change; a cancelled start opens nothing more and releases
   the microphone. This is what stops Card A's connection leaking into Card B.

**What fails over and what does not**

| Signal | Next provider? |
| --- | --- |
| Create call: 503 (any `live_voice_provider_*`), other 5xx, network error (status 0), 408 | Yes |
| Leg fails before live: peer connection failed, disconnected or closed, provider error event, socket error or close before setup, 15 s deadline, no WebRTC | Yes |
| Any other 4xx on the create call: consent missing, session not active, content not ready, 400 SDP, 409 `live_voice_time_limit_reached` or `live_voice_session_limit_reached` | No: terminal, the server's own text is shown |
| 429 `rate_limited` (our per-user limiter, not the provider) | No: one retry on the same provider after about 1.5 s |
| Microphone refused or missing | No: a device problem (shown with the microphone message ([wording](#microphone-error-wording)), which on the live and recorder panels ends "press Start speaking again", and, in the apps, an "Open app settings" button) |
| After the connection was live, or once any transcript text exists | Not at start: a dead link is restored by [Mid-session recovery](#mid-session-recovery) (same provider first, the other one on the second restore); the saved transcript stays one list on one clock ([Transcript time base](#transcript-time-base)) |
| Run pinned by the server (`pinned: true`, QA accounts only; the page's `?voiceProvider=` only asks) | Never |

The create calls are never retried by the API client (`maxRetries: 0`, 22 s timeout): a repeat POST after a
post-creation failure could open a second billed session, and the failover chain is the retry.

**Timing budget.** Preflight is a normal GET. Per provider: OpenAI gathers ICE for up to 5 s; the create call is bounded
server side by `LIVEVOICE__PROVIDERREQUESTTIMEOUTSECONDS` (default 10 s, clamped 2-20 s), which always ends before the
browser's fixed 22 s create-call timeout, so a slow provider ends in the server's `live_voice_provider_timeout` 503 (the
client fails over) and never in a client-side timeout; the link then has 15 s to go live. That is about 30 s per
provider with the defaults (5 + 10 + 15) and at most 42 s by the client's own bounds (5 + 22 + 15): about 60 s for two,
84 s at most. The typical failover is short: a provider that answers 503 or 429 does so in a fraction of a second, and
Gemini is ready in 2-3 s. On the exam the discussion clock is server-driven (300 s by default), so time spent connecting
comes out of the candidate's card: that is why the create call is not retried and why the deadline is per attempt.

**What the learner sees.** Nothing between attempts. If every candidate fails: one generic message ("The live AI
patient could not start. Please try again.") and one "Start speaking" button, which retries the whole chain and asks
the server for a fresh order first. Server 4xx text is shown as written. Provider names, provider bodies and transport
detail never reach the UI. The provider that served is exposed only as `data-live-provider` and `data-live-failover`
on the mic indicator (for the harness and support screenshots), and the saved transcript already records
`realtime-<provider>`.

**Exam.** Each card has its own hook instance, preflight, provider session and transcript, so failover is per card and
Card B asks the server again (it uses OpenAI again if OpenAI recovered). A requested `?voiceProvider=` pins both cards only
for a flagged QA account (each card's preflight answers `pinned`); for anyone else it is ignored. Card A and Card B can
therefore end up on different providers; grading reads transcripts only.

**Limits of the design.** A provider session that was created but never connected cannot be closed from the browser. For
OpenAI the server hangs it up with the rest of the role-play's sessions at the hard stop
([Hard duration cap](#hard-duration-cap)), so it can bill until then. An unused Gemini token cannot open a new socket
after `LIVEVOICE__GEMININEWSESSIONLIFETIMESECONDS` (60 s) and expires at the latest at the hard stop + 15 s. Consent copy
is generic, so a silent switch changes the data processor under it ([Consent and disclosure](#consent-and-disclosure)).

**Recovery sessions.** A provider session minted after an earlier one in the same role-play (the browser re-mints when the
connection drops or the patient goes silent) starts with no memory, so its instructions get a `CONVERSATION SO FAR` block
(`LiveVoiceService.ComposeInstructionsAsync`, both providers): the saved `realtime_turn` rows as `Candidate:` / `Patient:`
lines, oldest first, capped at 4000 characters (the newest turns that fit, with an `(earlier turns omitted)` line when older
ones were dropped). The browser flushes the turn in progress before it asks, the first mint of a role-play is unchanged, and
the history goes to the provider only (never logged, audited or returned); each recovery mint still counts towards
`MaxProviderSessionsPerRolePlay` ([Hard duration cap](#hard-duration-cap)). Verified in production on 1 Oct 2026: after the injected drop the patient carried on coherently (it answered the questions asked after the recovery).

### Microphone error wording

`describeMicrophoneError(error, native, { live: true })` (`lib/mobile/speaking-recorder.ts`) words a microphone start failure
for the live AI patient: the learner speaks (the control reads "Start speaking") and nothing is called a recording. The live
variants are "The microphone could not start: ..." (an error that carries a message), "This microphone does not support the
requested audio settings. Try another device.", "This browser does not support the audio mode needed for Speaking practice."
and, for a blocked permission, a recovery that ends "then press Start speaking again". The live hook passes the option itself
and no longer rewrites the recorder text; without the option the wording is the recorder's, unchanged. **Pending: not yet
verified in production.**

## Mid-session recovery

Gemini sessions on 30 Sep 2026 went silent after ~2.5 min (patient never answered again) or were closed by Google with 1011
"Resource has been exhausted" / "Internal error"; with no recovery the learner's conversation simply ended. The hook
(`hooks/useSpeakingRealtimeVoice.ts`, `startRecovery`) now restores the link on a **new provider session for the same
role-play**; the microphone, its context and the transcript carry on untouched, and the server replays the saved turns
into the new session's instructions ([Recovery sessions](#provider-failover)).

- **Triggers.** (1) The link dies after it was live: Gemini socket close or error event; OpenAI data channel close, a
  peer connection `failed` (a `disconnected` link gets 5 s to heal first), an `error` event, or `session.closed` with a
  reason other than `close_requested`, `expired` or `content` (the provider's own time or content end is final).
  (2) A **stall**: the candidate finished a real sentence (a microphone burst of at least 1 s) and the patient produced no
  audio or transcript for **20 s** (`STALL_MS`; healthy replies take ~2-3 s, the slowest healthy one seen was ~19 s). The
  20 s run from the **oldest** sentence the patient still owes an answer to: a candidate who keeps talking to a silent
  patient ("Hello? Can you hear me?") does not push the restore back (the first production stall run, 1 Oct 2026, restored
  94 s after the stall because every new sentence restarted the clock).
- **Procedure.** Flush the turn in progress (so the server's history is complete) -> tear down the dead transport ->
  mint a new session: the **same provider first**, then the other one; on the **second** restore of a card the other
  provider goes first. A definite server answer (409 time over or session limit, any other 4xx) stops the attempts and shows
  "The live conversation ended ...". The panel shows "Reconnecting the patient…" meanwhile and exposes
  `data-live-recoveries` for the QA harness.
- **Limits.** At most `MAX_RECOVERIES` = 2 per card (the server allows 3 provider sessions per role-play: the first plus two
  restores). A run the server pinned (`pinned: true`, QA accounts only) **never recovers**, so comparison and QA runs
  measure the raw stability of the provider they asked for; a `?voiceProvider=` the server did not honour changes nothing
  here. No restore after the learner pressed stop or left.
- **Known limits.** Audio spoken while the link is down is lost. The candidate's last unanswered sentence is in the replayed
  history, so the patient waits for the candidate to speak next; a candidate who is waiting for the answer must speak again.
  The stall detector reads the microphone level (no echo handling beyond the browser's echo cancellation). The saved
  transcript keeps one clock across a restore; the remaining offsets are listed under
  [Transcript time base](#transcript-time-base). A page reload is not a link drop: see
  [Refresh behaviour](#refresh-behaviour).
- **Measured in production** (1 Oct 2026, Gemini, build 679e29cdc): a dropped link was restored 0.0 s (RUN 2, drop at 40 s of a
  practice card) and 0.3 s (RUN 4, drop at 45 s of Card A) after the drop, a stalled link (RUN 3, frames swallowed from 40 s) 8.0 s
  after the stall began, and a link that went silent by itself (RUN 1) 26 s after its first unanswered sentence. The sentence being
  answered when the link dropped got no reply (RUN 2) and what was said while a link was silent was never transcribed (RUN 1,
  RUN 3): see [Known open items](#known-open-items). The OpenAI figures (a drop restored 0.6 s later, a stall after 35 s) are from
  the 1 Oct runs before the time base change.

### Gemini candidate timing

Gemini gives the candidate's transcript no timing; every candidate segment used to be zero-length (start = end = the moment
the text arrived), which the grader flagged ("the candidate segment has zero duration ... may be a capture error") and which
hid the real order of speech. The hook now finds the candidate's speech bursts from the microphone level
(`createSpeechTracker`: level >= 0.05 on the meter's scale, a burst ends after 700 ms of quiet, bursts under 400 ms are
dropped) and gives a transcript fragment the span of its burst: the closed bursts since the last fragment, or the burst so
far when the text arrives mid-sentence. Patient segments keep their arrival time. OpenAI keeps its own `start_ms`/`end_ms`,
shifted by the session offset; every source is then on the role-play clock ([Transcript time base](#transcript-time-base)).
**Verified in production on 1 Oct 2026** (RUN 4: no zero-length candidate segment on either card and every aligned candidate
line within 0.05 s of the played script). Every Gemini transcript saved before it (G1-G4, E4, P1, U1) has zero-length candidate
segments (for example G4: 10 of 12 on Card A, 14 of 14 on Card B).

## Saved transcript

The transcript is built in the browser while the conversation runs (`hooks/useSpeakingRealtimeVoice.ts`), saved once when the
role-play stops and graded verbatim. This section says what its times mean and how it survives a restore, a reload and a stop.
Everything in it was added on 1 Oct 2026. The one clock and the refresh copy were **verified in production the same day**
(OpenAI RUN 5 and RUN 6; a Gemini drop restored inside one clock on RUN 4 Card A and RUN 2; no split sentence on any run); the
pause rule, the whitespace rule and the Gemini stop drain are covered by unit tests only, and what Gemini still gets wrong is
listed under [Known open items](#known-open-items).

### Transcript time base

Every segment time in the saved transcript is milliseconds since the **first** provider session of that role-play went live
(the role-play clock; close to the card start, because the panel connects when the card opens). GPT-Live's `start_ms` and
`end_ms` count from the start of its own provider session and restart at 0 in every restore, so the browser adds the offset
between the first link going live and the current one (`sessionOffsetMs`). Gemini gives the candidate's transcript no timing:
candidate spans come from the microphone bursts ([Gemini candidate timing](#gemini-candidate-timing)) and patient segments
from the arrival time of the chunk, both converted to the same clock. A restore (on the same provider or the other one) and a
manual restart keep the origin, a reload rebuilds it from the stored copy ([Refresh behaviour](#refresh-behaviour)) and only a
different Speaking session starts a clock of its own.

Result: after an OpenAI restore the saved list alternates candidate and patient, `startMs` is non-decreasing and nothing is
merged across the restore. Before this, GPT-Live's clock restarting at 0 made the late-fragment rule merge everything after a
restore into earlier segments: the three 1 Oct recovery runs F1, F2 and F3 saved 16, 8 and 14 segments instead of the 24, 12
and 20 of a correct timeline, with five scripted lines fused into one candidate segment on F1 and F3, while every check of
those runs was green. Gemini times are card-relative too (the first segment is near 0, not at the page's uptime).

Known remainder: OpenAI's own clock starts at provider-session creation while the origin is the moment the link goes live, so
its times carry the connect delay (about 1-6 s) as a constant offset. Across an OpenAI restore only the difference of the two
connect delays is left (0.1-0.3 s when the saved events of F1-F3 are replayed), and across an OpenAI to Gemini switch a `startMs` can step
back by up to the first session's connect delay (nothing is merged). Gemini patient durations are still arrival-based (about
3.5 times too short).

### Pause rule

A fragment from the same speaker extends that speaker's last segment only when the gap between the fragment's start and that
segment's end is at most 10 s (`MAX_SAME_SPEAKER_GAP_MS` = 10 000). After a longer silence a new segment starts, so the pause
is visible to the grader and to the v1.1 evidence capture (pause count, monologue flag) and the candidate's talk time is not
inflated (1 Oct: 45 s of silence sat inside one 54 s candidate segment). The late-tail rejoin (a candidate's last word arriving
after the patient's backchannel, see [Behaviour and limits](#behaviour-and-limits)) is unchanged and ignores this limit.
Consequence: two consecutive segments by the same speaker are legitimate when more than 10 s of silence lies between them.

### Whitespace deltas

GPT-Live sometimes sends the space between two words as a delta of its own. It is appended to the end of the **same**
speaker's last segment (so " Doctor." + " " + "Well," is saved as "Doctor. Well,"), never starts a segment (the server rejects a
blank segment and would fail the whole save), never takes the late-tail path and never moves the segment's end. Before this
such a delta was dropped and the two words were fused in the saved text ("Doctor.Well" on F1, "butI" on O4).

### Refresh behaviour

While a provider session is live, the browser keeps a copy of the conversation in this tab's `sessionStorage` under
`oet.speaking.live.<speaking session id>`: `{sessionId, segments, turnIndex, originEpochMs, savedAt}`. Only the conversation of
that session and the numbers needed to carry on are stored (no provider session id, no token).

- **Written** at most once a second while the conversation changes, immediately when the page is hidden (`pagehide`,
  `visibilitychange` to hidden) and once more when the card is left.
- **Restored** when the hook mounts again for the **same** session id and the copy is younger than 15 minutes
  (`CHECKPOINT_TTL_MS`): the segments and the turn number come back and the role-play clock is rebuilt from `originEpochMs`, so
  the new provider session lands after everything that was restored. A copy that is malformed, belongs to another session or is
  older than 15 minutes is ignored and removed.
- Starting again after a reload still mints a **new** provider session (the server replays the saved turns into it, see
  [Recovery sessions](#provider-failover)); the combined transcript is saved at the end under that new provider session id.
- **Removed** after the transcript POST succeeds or when the server will never take it (a 4xx other than 401, 408 and 429); a
  failed save worth retrying keeps it.
- Turn ids are `voice-turn:<per-mount random tag>:<n>` and the number continues from the restored value, so two page instances
  never reuse an id that the server would drop as a duplicate.
- If `sessionStorage` is unavailable everything behaves as before (memory only).
- **Not covered:** a reload that is never followed by Start speaking, a closed tab or a crash still save no transcript (a stored
  copy cannot be POSTed without a live provider session id; see [Known open items](#known-open-items)). A reload may leave the
  panel waiting for a tap on "Start speaking" (the browser's gesture rule), and each reload uses one of the 3 provider sessions
  per card. A browser that copies `sessionStorage` into a duplicated tab can restore the same copy there (the server still
  accepts one active role-play per session). The copy holds the words of both speakers, in that tab only, and no provider
  session id or token.
- **Measured in production** (1 Oct 2026, RUN 5, a real OpenAI two-card mock refreshed at 60 s of Card A): the panel came back
  and started by itself 5.7 s after the reload (no tap, `autoStarted` true, `providerAfter` openai), the restored conversation
  still held the turns spoken before the reload (`reloadKeepsTranscript`), the saved Card A transcript had 30 segments with no
  split sentence and passed every [transcript check](#transcript-checks), and the mock cost 4 credits once (a refresh costs no
  second hold): Speaking pool 36 -> 34 -> 32 -> 32.

### Stopping a Gemini role-play

Gemini transcribes a sentence about 1.5 s after it was spoken and has no close handshake, so `stop()` keeps a Gemini link (and
its handlers) up for `GEMINI_STOP_DRAIN_MS` = 2 s before it closes it and saves the transcript: a candidate still talking at the
buzzer keeps the end of the last sentence. The wait is skipped when the socket is not open, and GPT-Live drains through its own
`session.closed`. A Gemini card therefore saves its transcript about 2 s later (the exam page already polls). No production run
has had speech in flight at the close yet, so neither drain has been exercised by data.

## Results wording by input kind

Live voice stores consented short microphone clips captured during detected learner speech and linked to verified candidate
transcript evidence—not a full-session recording. A results page that says "We received your recording" is still wrong for
it. The pages therefore say what was actually handed in. Browser echo cancellation is enabled, but cannot guarantee that
provider or background audio is excluded from a microphone clip.
**Transcript wording was verified in production on 1 Oct 2026** by the production E2E (`inputKindLiveVoice`,
`resultsWordingHonest` and `noProviderNamesInUi` green on RUN 1, RUN 5 and RUN 6, see [Credits, History and wording
checks](#credits-history-and-wording-checks)); those runs predate consented microphone-clip capture and do not verify the current
clip path.

- **Server: `inputKind`.** `GET /v1/speaking/sessions/{id}/results` returns it besides `assessmentState`, `retryable`,
  `failureReason`, `isFreeSample`, `cardId` and `usesV11` (camelCase; `null` is serialised as JSON `null`):
  - `"recording"`: the session has a non-warm-up recorder or tutor recording, archived or not;
  - `"live_voice"`: its latest transcript was saved by the live voice flow (its `Provider` starts with
    `LiveVoiceService.TranscriptProviderPrefix`, `realtime-`) or it has a non-warm-up candidate clip from
    `ConversationHub`; linked microphone clips may exist, but the app does not make a full-session recording or directly
    record provider playback;
  - `null`: otherwise, meaning nothing has been received yet (a recorder card before its upload lands, or a live transcript
    save that failed or has not arrived) or an older server.
- **Browser.** One pure module, `lib/speaking/input-kind.ts`, decides the wording: `speakingInputKind(isTutorRoom, serverKind)`
  is `"recording"` for a tutor room (session mode `live_tutor`, always recorded), otherwise the server's value, otherwise
  `null`. `null` always gets the neutral "role-play" wording; the recorder fallback and tutor rooms keep the original recording
  wording character for character. A retry ("Try grading again") keeps the kind.
- Persistent results copy never contains "processing", "being graded", "analysing" or "Check again": the production QA script
  (`waitForGrade` in `scripts/qa/speaking-live-voice-browser-e2e.mjs`) reads those words as "still grading".
- **Transcript tab and report.** `TranscriptPlayerWithComments` takes an opt-in `hideAudioPlayer` (default `false`: the expert
  console is unchanged). The learner page passes `hideAudioPlayer={inputKind !== 'recording'}`, which hides the full-session
  player and turns the `[mm:ss]` chips into plain text. For `live_voice`, the V1.1 report view says "Transcript of your live
  conversation", explains that it is not a full-session recording and that mic clips may pick up speaker audio, and offers
  clips only for verified source-linked evidence; playback uses the source session and clip-relative offset. Its
  technical-review notice correctly identifies missing transcript/assessment evidence rather than blaming absent
  full-session audio. `null` stays neutral.
- **Backend texts that changed** (learner-visible). The live voice consent refusals now read "Accept the Speaking consent before
  starting the live AI patient." and "Accept the current Speaking consent before starting the live AI patient."; the grading
  failure reasons read "We couldn't finish grading your role-play. Try grading again. You won't be charged twice." and "Your
  role-play could not be scored automatically. Try grading again." The recorder-only texts "No speech could be detected in the
  recording." and "We couldn't transcribe your recording. Try grading again." are kept.
- **Grader.** For a transcript whose `Provider` starts with `realtime-`, the classic grader's input carries one extra line before
  the transcript saying that the role-play has no full-session audio recording and that the feedback must never tell the
  candidate to listen to or check a recording. Grading remains transcript-only; candidate clip capture supports playback and
  v1.1 acoustic assessment, not a change to the classic grader. The template id (`speaking.score.v2`), rubric, scoring rules and output schema were
  unchanged by that change (the template moved to `speaking.score.v3` on 4 Oct 2026 — see [scoring.md](scoring.md)). The gateway's request digest covers the input, so a live-voice grade that straddles the deploy can be asked of the
  provider twice; the stored assessment is still deduplicated by its identity hash and the hold is committed once.
- **Not changed on purpose** (owner/legal decision): the Rules and consent screen (it still reads "Your audio is recorded and
  graded by AI" before each card), the consent versions and the backend consent and retention texts.

Copy by kind:

| | Live conversation (`live_voice`) | Recording (`recording`) | Not known (`null`) |
| --- | --- | --- | --- |
| "Submission received" banner body (practice sessions only; an exam card is never submitted) | We saved the transcript of your live conversation{ on DATE} and queued it for marking. | We received your recording{ on DATE} and queued it for marking. | We received your role-play{ on DATE} and queued it for marking. |
| While grading | Your live conversation transcript is being marked. This page updates automatically. | Your recording is being transcribed and marked. This page updates automatically. | Your role-play is being marked. This page updates automatically. |
| Failed grade, fallback body (the server's own reason is shown when it sends one) | Your transcript is saved. No credits were used for this failed grade. | Your recording is saved. No credits were used for this failed grade. | Your role-play is saved. No credits were used for this failed grade. |
| Exam notice once polling gives up (named by the kind all cards share, otherwise role-plays) | Grading is taking longer than usual. Your transcripts are saved and the result will appear here. | Grading is taking longer than usual. Your recordings are saved and the result will appear here. | Grading is taking longer than usual. Your role-plays are saved and the result will appear here. |

### Exam results page

`/speaking/exam/{id}/results` (the readable band, the advisory sentence and the absence of "recording" wording were **verified
in production on 1 Oct 2026** by the RUN 5 and RUN 6 wording check; the other items below are covered by component tests only):

- shows the readiness band by label (Not yet ready, Developing, Borderline, Exam ready, Strong) with a tone by band, never the
  raw code;
- shows "AI practice estimate, not an official OET result." (a tutor-marked exam says "Practice estimate, not an official OET
  result.");
- each card has "View details and transcript" (to `/speaking/sessions/{cardSessionId}/results`), and "View history" (to
  `/submissions`) sits next to "Back to Speaking";
- an exam whose state is expired or cancelled shows "This exam was not completed." with "Start a new exam" (to
  `/speaking/exam`), stops polling and shows no spinner;
- a failed card shows the server's `failureReason`, and a failed "Try grading again" shows its error under the card (a browser
  timeout is not an error: grading continues on the server and the next poll shows it).

Exam-card pages show no submission banner (the card's `submittedAt` stays empty): for cards the live wording appears while
grading and in the Transcript tab note. Other copy was aligned the same way: the Speaking tour ("How your answers are saved"),
the recordings page (which now lists consented live microphone clips and distinguishes them from tutor/recorder audio),
the AI tooltip ("based on your transcript") and the admin "Submission received" toggle.

## Consent and disclosure

- The learner-facing consent step (`SpeakingRulesConsent`) shows generic copy ("... your microphone is streamed in real
  time to the live voice provider"). It names no provider, and the client does not render the preflight `disclosure`.
- The server's preflight `disclosure` names **every** candidate, in try order, each with its model. With two candidates it
  names the first, says the service may switch to the next one if the first cannot start the conversation, names that
  one too and says the microphone audio goes to "whichever of these providers serves your session". With one candidate
  (pinned through `?voiceProvider=` for a flagged QA account, or only one healthy) it names that provider only, as before.
  It no longer names only the primary.
- A silent failover therefore still changes the data processor under generic consent copy. Whether that needs a new
  consent step (or rendering `disclosure`) is an **owner/legal decision** and is open.

## Provider health

Health decides **which** candidates are offered, never their order: the order is always the configured primary first,
then the other. It is driven by **real session-creation outcomes**, because a `/models` 200 does not prove a session can
be created (30 Sep: OpenAI's catalog probe was green while session creation answered 429).

- Every create call (the OpenAI offer, the Gemini token) is classified from the provider's HTTP status and its own error
  type/code tokens (`AiProviderErrorParser`, the classes the grader also uses). For an OpenAI or Gemini 400, 413, 415 or
  422 (replies that can echo the learner's SDP or the card instructions) message phrases such as "credit balance" or
  "spend limit" are ignored; they still count for Anthropic and for 402, 429 and 5xx, and a Gemini FAILED_PRECONDITION
  "billing" 400 stays a quota failure (it is anchored on the provider's own status token).
- What each class does to the circuit (`LiveVoiceProviderProbeState`; a success closes it and resets the failure run):

  | Class (typical HTTP) | Effect |
  | --- | --- |
  | `quota_exhausted` (402, quota tokens), `auth` (401, 403, invalid key) | opens at once for **10 minutes** |
  | `rate_limited` (429) | opens for the provider's own `Retry-After`, clamped to 5-120 s (30 s when it sends none) |
  | `overloaded` (503, 529), `server_error` (5xx, 408), `network` (timeout, transport fault), `unknown`, and an unreadable 200 (`invalid_response`) | opens for **60 s** once two land within 60 s (at once during probation) |
  | `invalid_request` (any other 4xx: 400, 404, 413, 415, 422, ...) | counted, never opens: one browser's malformed SDP or one card's request must not take a provider away from everybody |

- After an open period the provider is on **probation**: the next real failure (other than `invalid_request`) re-opens it
  at once, the next success closes it. There is no single-probe gate, so learners arriving together all try it.
- The `/models` probe (every 5 minutes; every 60 s while a configured provider is unverified or open) is only a **catalog
  gate**: a provider without a passing probe is not offered, but a passing probe never closes a circuit that real
  failures opened. A transient probe fault (rate limit, overload, server error, network) keeps an already verified
  provider verified; a first-ever transient failure leaves it unverified (fail closed); auth, quota, a missing model and
  anything unclassified verify nothing.
- Candidates are `[primary, other]` filtered by configured, verified and not open (closed or on probation). No
  candidate: preflight answers 503 (`_not_configured` when nothing is configured, otherwise `_unavailable`, retryable) and
  the session and exam DTOs report `liveVoiceAvailable: false`, which selects the recorder fallback at page load. A
  pinned request (QA accounts only) bypasses the circuit (it still needs the provider configured and verified) and its
  outcome is still recorded.
- State is in memory in the API process that serves learners (one upstream at a time). A restart, a deploy or a
  blue/green swap starts from a clean state: the cost is at most one fast failed attempt per hard-failing provider,
  failed over transparently.
- `/health/ready` never reads the **circuit** (readiness gates deploys and rollbacks; a provider outage must not become a
  deploy outage). It does read the catalog gate: in Production with `FEATURES__SPEAKINGV2=true` (compose default `false`)
  it requires **both** providers to be configured and probe-verified (`live_voice_openai` and `live_voice_gemini` are `ok`,
  `unverified` or `not_configured`) and LiveKit recording to be configured.
- Admin (`AdminAiConfig`, per-user rate limit): `GET /v1/admin/ai/live-voice/health` shows `candidateOrder` and, per
  provider, whether it is configured and the primary, the catalog probe (verified, reason, checked at), the circuit
  (`closed` | `open` | `probation`, open-until, consecutive failures), the last failure (class, HTTP status, the provider's
  error type and code, time), the last success and the counters (attempts, failures, failures by class).
  `POST /v1/admin/ai/live-voice/{provider}/reset` (`openai` | `gemini`, else 400 `live_voice_provider_invalid`) closes a
  provider's circuit without a success, for example after a top-up or a key rotation; it answers
  `{provider, breaker, wasOpen}` and is audited. Both read the process that answers, like the state itself.
- **Audit events** (`AuditEvent`, resource type `LiveVoiceProvider`, resource id `openai` | `gemini`), one per transition
  and never per failure: `LiveVoiceProviderCircuitOpened` and `LiveVoiceProviderCircuitClosed` (actor `system`; details:
  class, HTTP status, provider code, open-until, consecutive failures) and, from the admin reset,
  `LiveVoiceProviderCircuitReset` (actor: the admin; details: `wasOpen`). The role-play hard stop writes
  `SpeakingRolePlayHardStopped` ([Hard duration cap](#hard-duration-cap)).
- **Logs.** One line per failed create call, `Live voice {Provider} session creation failed:` followed by the fields
  `class`, `http`, `type`, `code`, `requestId`, `elapsedMs`, `breaker` (`None`, `Opened` or `Closed`) and `providerError`,
  and `Live voice {Provider} circuit closed after a successful session creation.` when a success closes a circuit. The
  level is **Error** only where an operator has to act: `quota_exhausted`, `auth`, and `invalid_request` with an HTTP status
  other than 400/422 (an unknown model or URL 404, an unsupported media type 415). Everything else (rate limits, overload,
  server errors, network faults, timeouts, unreadable answers, and a 400/422 shaped by the learner's own request) is
  **Warning**, so one learner cannot raise Error-level alerts at will. Error-level lines are what Sentry reports when a
  DSN is configured (nothing in the repo changes the SDK default).
- The provider's own message (redacted, one line, at most 300 characters) is kept in `providerError=` **only for HTTP 429
  and 5xx from `api.openai.com` and `generativelanguage.googleapis.com`**. It is an allow-list: any other status or host
  logs the class, status, error type/code and request id but never provider text (a 400, 413, 415 or 422 can echo the
  request, a 401 or 403 a masked key, a 404 the URL), and the catalog probe never keeps provider text. Keys, tokens, SDP,
  card or hidden instructions and transcripts are never logged.

## Admission control (live session cap and wait queue)

Owner decision 5 Oct 2026: at most **100** live AI patient sessions run at once; beyond that a learner waits in a FIFO line
and is started when a place frees. Source: `SpeakingLiveAdmissionService`, tables `SpeakingLiveAdmissions` and
`SpeakingLiveAdmissionSettings` (migration `20270109113000_AddSpeakingLiveAdmission`), options `Speaking:LiveAdmission`
(`SpeakingLiveAdmissionOptions`). Provider health above decides **which** provider; this decides **whether a session starts now**.

**Verification status: not tested - owner QA** (owner directive 2026-10-06: no automated QA runs anywhere). The only automated
check on this layer is compilation inside `Build images` (`dotnet publish`, `next build`) plus the EF pending-model-changes
check on the hand-authored snapshot entries; nothing here was built, benchmarked or run locally. The test sources that describe
the intended behaviour (`SpeakingLiveAdmission*Tests.cs`, `AdminOpsSnapshotServiceTests.cs`, `SpeakingAdmissionWait.test.tsx`,
`speaking-admission.test.ts`, the exam and role-play page tests) stay in git as inert manual tools: no CI lane runs them and none
has been run for this change. The first real proof is the owner's own testing of the wait queue on production, and the admin
kill switch below is the lever if it misbehaves.

- **Where.** At the two unscored gates that precede every credit hold and every clock: an AI **exam** at
  `POST /v1/speaking/exams/{id}/finish-intro` (one place for both cards; Card B's reveal is not gated) and an AI **practice
  card** at `POST /v1/speaking/sessions/{id}/finish-warmup` (the free sample too: it uses live voice). Live-tutor exams and
  sessions are never gated. The gate runs after the plan check and before the hold. **A learner who cannot pay for the hold is
  refused with the hold's own 402 before taking a place or a line position**, for both an exam (`speaking_exam_insufficient_credits`
  when the wallet is too small; `ai_credits_insufficient` for an account with no credit wallet at all) and a practice card
  (`ai_credits_insufficient` / `no_ai_package_credits`): a read-only mirror of the hold (`AiCreditReservationService.ReserveSpeakingAsync`
  where reservations are wired, as in production). The designated free-sample card holds no credit and skips it. The check runs
  only when a call is about to take or look for a place (`beforeNewPlace`), never on a waiting learner's repeat poll.
- **While waiting.** The call answers **200** with the unscored state unchanged (exam `intro`, practice `warmup`) and an
  `admission` object `{status:"waiting", position, queueLength, estimatedWaitSeconds, pollAfterSeconds}`. Nothing is timed, **no
  credit is held**, no child session or Card B is created, and `PrepAStartedAt` / `PrepStartedAt` stay null. A plain
  `GET /v1/speaking/exams/{id}` or `GET /v1/speaking/sessions/{id}` shows the same `admission` (read-only: it never refreshes the
  heartbeat). An admitted learner gets the normal next state and **no** `admission`.
- **How a waiter is admitted.** The learner page (`SpeakingAdmissionWait`) repeats the same finish call every
  `pollAfterSeconds` while the tab is visible (4 s, growing by 1 s per 25 waiters up to 20 s for a very long line), every 20 s
  while hidden, and at once when the tab becomes visible. That call is also the heartbeat. A poll of a learner who is already
  waiting and still cannot be admitted takes **no lock**: it refreshes its own heartbeat and reports its place (about six small
  indexed queries). Only an enqueue, an admission or an expiry takes the advisory lock, and only that path can admit, so a long
  line polling never queues on the lock. The call that finds a free place **holds the credit and starts the clock in that same request**:
  admission is atomic with the start. While the wait panel is on screen the exam page runs **no refresh poll of its own** (the
  panel's retries carry the newest place, and two pollers would race and could show a stale answer); a 409 on a retry other than the
  one-place refusal means the exam moved on (admitted in another tab, cancelled), so the page re-reads it. A refusal at the gate
  (no credits, another live session, a full line) is kept in its own state on screen: a later successful poll no longer wipes it.
- **Atomic and FIFO.** Every decision runs under one Postgres advisory lock inside a short transaction of its own
  (cross-process: both API slots and the ai-worker) that waits at most **5 s** for the lock (`SET LOCAL lock_timeout`): a hung
  holder becomes the fail-open path below, never a pooled connection waiting for ever. A waiter holds a strictly increasing
  ticket; a caller is admitted only when its rank among live waiters is below the number of free places, so a newcomer never
  overtakes an earlier waiter even while that waiter's page has not polled yet. One row per exam or practice session
  (`{kind}:{id}`), so a subject can never hold two places. **A caller that already owns a database transaction is never
  gated** (outcome `Bypassed`, reason `ambient_transaction`; `SweepAsync` does nothing): the lock is transaction-scoped and would
  be held, platform-wide, until that foreign transaction ends. The admin corpus harness is such a caller (one transaction per
  card, rolled back).
- **One live place per learner.** A learner holds at most one live place (waiting in the line, or holding a slot) at a time. A
  request for a **different** subject while another place is waiting (fresh heartbeat) or holding a slot is refused with
  **409** `speaking_live_session_active` ("You already have a live Speaking session open or waiting. Finish it, or leave the
  queue, before starting another."), before any line position, hold or clock, so one account cannot fill the line or the cap by
  creating many sessions. Known consequence: a learner who abandons a practice card in its preparation window and picks
  another card is refused until the first one stops holding a place (its card ends, or its 20 min safety TTL passes); there is
  no "abandon card" action yet. The page treats this 409 as a message to show, never as "already admitted"
  (`isAlreadyPastGateConflict`).
- **What holds a place.** An admitted row inside its safety TTL (exam 45 min, practice 20 min) that was admitted within the
  120 s claim window or whose subject is running (exam `prep_a` .. `active_b`, practice `prep` / `active`). A finished,
  cancelled or expired subject therefore frees its place at once with no release hook on any terminal path. A running session
  is **never evicted**: lowering the cap only stops new admissions until the count drains. An admitted row that **no longer
  holds** (its claim window is over and the subject never started: a refused hold, a lost response) is **not** admitted again
  for free: its repeat call asks for a place again with a fresh ticket and the cap is re-checked like anyone else's.
- **Giving a place back at once.** (1) A start that fails after the call took the place (a refused or failed credit hold)
  releases it immediately (`ReleaseAsync`, called by `FinishIntroAsync` / `FinishWarmupAsync` only when that call took the
  place, never for an idempotent repeat). (2) Cancelling an exam releases its place. (3) `POST /v1/speaking/exams/{id}/leave-queue`
  and `POST /v1/speaking/sessions/{id}/leave-queue` (the wait panel's "Leave the queue", owner-checked, 204) release a waiting
  place so it stops counting towards the positions behind it, instead of lingering for the 90 s heartbeat; the exam or card stays
  in its intro / warm-up. (4) A waiter whose turn has come but who can no longer pay is refused with the credit 402 and leaves
  the line at once, so nobody waits behind a ticket that cannot be used. A release is one single-row UPDATE (no lock, cannot
  flush the caller's pending changes, never throws) and never touches a subject that is running; if it fails the claim window
  frees the place anyway.
- **Abandoned waiters.** A waiter silent for 90 s leaves the line (its ticket stops counting). A polling waiter is **not**
  demoted for waiting long: the line is never allowed to grow longer than the maximum wait can serve at the average session
  length (`MaxLineLengthFor(cap) = min(MaxQueueLength 1000, MaxWait 7200 s x cap / AverageSessionSeconds 900)`; 800 at cap 100), so a
  waiter that keeps polling is served before the 2 h safety bound, which exists only so a forgotten tab cannot hold a place and
  start a session unattended (past it the waiter goes to the back). A new waiter beyond the line limit gets a retryable **503**
  `speaking_live_queue_full` instead of a wait the server could not honour; the page's estimate is capped at the maximum wait.
  The sweeper (`SpeakingExamAutoAdvanceWorker.SweepAdmissionsAsync`) only tidies the table (expires, purges rows older than 7
  days); correctness never depends on it.
- **Degrade path (explicit).** With **no healthy live provider** (`liveVoiceAvailable` false) the learner uses the recorder
  fallback, which consumes no live capacity: the gate is **bypassed**, never queued, and writes no row. If the gate itself fails
  (a database fault, a missing table, a lock wait that timed out) it **fails open**: the learner is let through and an Error is
  logged. A capacity check is never the reason a paying learner cannot start. The recorder fallback itself is unchanged.
- **Kill switch and cap (no restart).** `GET /v1/admin/ai/live-voice/admission` shows `{enabled, maxConcurrent, source
  (default|admin), admitted, waiting, free, oldestWaitingSince, oldestWaitSeconds}`. `PUT /v1/admin/ai/live-voice/admission`
  with `{"enabled":false}` lets everyone through at once (today's behaviour; waiters are released on their next poll) and
  `{"maxConcurrent":150}` sets the cap (1..10000); either field may be omitted. Both are `AdminAiConfig`, audited as
  `SpeakingLiveAdmissionSettingsUpdated`. **The kill switch never waits on the decision lock**: with it off every call returns
  `Bypassed` straight away and a waiting learner's row is released by a single-row UPDATE, so it still works when a lock holder
  is stuck. With no row the cap is `Speaking:LiveAdmission:DefaultMaxConcurrent` (100). **The admin switch is the only
  operational lever in production**: `docker-compose.production.yml` has a closed environment list and forwards no
  `Speaking__LiveAdmission__*` key, so those environment keys (including `Enabled=false`) do not reach the API or the ai-worker
  ([../env/speaking.md](../env/speaking.md#live-ai-session-admission-has-no-environment-keys-in-production)).
- **Counts are database-derived** (all API slots and the worker), not a process gauge: they are also in
  `GET /v1/admin/ai/live-voice/health` as `admission`, and in `GET /v1/admin/ops/snapshot` (job queue depth by type, database
  connections by `application_name` from `pg_stat_activity`, admitted and queued counts, and a placeholder for remote workers;
  `AdminSystemAdmin`, read-only, counts only; connections read `(unset)` until each process sets its own `Application Name`).
- **Not capacity controls** (verified dead 5 Oct 2026): `SpeakingSimulationV11TurnTelemetryService.ActiveTurnCount` and the
  statics in `ConversationHub.SpeakingRoleplay.cs`. Every entry point of that legacy hub rejects typed sessions first, so they
  never leave zero; they are marked as dead in code.
- **Not done / owner decisions.** The estimate is `(place) x AverageSessionSeconds (900) / cap`, not measured. Human tutor
  rooms are LiveKit Cloud and not part of this cap. Provider-side concurrency limits (OpenAI GPT-Live, Gemini Live) are still to be
  confirmed against the cap. A learner who reloads the practice entry page while waiting loses the place (the page keeps the
  session id only in memory); the abandoned row expires.

## Hard duration cap

A role-play used to have no server-side limit: the 300 s was card data read by a client countdown, and a client that never
called `/end` left the session active for ever. The server now enforces it. (Numbers are the `LiveVoiceOptions`
defaults at the time of writing; that class is the source of truth.)

- **Deadline** = `RolePlayStartedAt` (stamped by `/start-roleplay`, or by the exam clock for an exam card) + the card's
  `RolePlayTimeSeconds` (300 if missing), capped by `MaxRoleplaySeconds` (600; clamped 180-1800; the exam's card timing
  uses the same capped seconds). **Hard stop** = deadline + `HardStopGraceSeconds` (30; clamped 0-120), covering the
  client's own stop-and-save. Both are computed from persisted data with no new column. `hardStopAt` is returned by the
  offer, the token and `GET /clock` (on `/clock` for an Active session only, null otherwise); `rolePlayEndsAt` on the
  session detail is the **deadline**, not the hard stop (null until the role-play has started; on create it is a
  forward-looking estimate). An Active row with no `RolePlayStartedAt` (a data anomaly) falls back to `UpdatedAt`, so no
  session is immortal.
- **No new provider session after the deadline** (409 `live_voice_time_limit_reached`), only while the session is Active
  (409 `live_voice_session_not_active` otherwise: an exam card in prep cannot open one), and at most
  `MaxProviderSessionsPerRolePlay` (3; clamped 1-10) per role-play (409 `live_voice_session_limit_reached`); a session
  counts once it was created (only a created session is recorded), so a failover after an OpenAI 429 does not burn the
  allowance.
- **Gemini**: the ephemeral token expires at `min(now + 1800 s, hardStop + 15 s)`. A token minted at the role-play start
  therefore expires at the hard stop + 15 s whenever the hard stop is at most 1785 s away (cards up to 1755 s with the
  default 30 s grace); beyond that it is capped at 30 minutes from the mint, which only `MAXROLEPLAYSECONDS` raised
  towards its 1800 maximum can reach. `LIVEVOICE__GEMINITOKENLIFETIMESECONDS` is kept only so existing config binds and
  has **no effect**: it can neither shorten a token (a 90 s value cut every conversation off on 25 Sep) nor lengthen
  one. `newSessionExpireTime`, the window in which the browser must open the socket, is
  `min(now + LIVEVOICE__GEMININEWSESSIONLIFETIMESECONDS, expireTime)` (default 60 s, clamped 15-120). Gemini closes the
  socket itself at expiry (1011 "auth token has expired", seen on 25 Sep) even if the browser never does.
- **OpenAI**: the media path is browser to OpenAI, so the server cannot cut a session with its own data. It is bounded by
  refusing new offers after the deadline and by the session cap, by the browser (finish at 00:00 and at the `/clock`
  `hardStopAt`) and by the server **hanging up** every OpenAI session minted for the role-play:
  - *The call*: `POST {LiveVoice:OpenAiBaseUrl}/{providerSessionId}/hangup` (default
    `https://api.openai.com/v1/live/sessions/{id}/hangup`), 5 s per call; any 2xx or a 404 (already ended) counts as
    ended; best effort, never fatal. The raw OpenAI session id is stored for it in the `live_session` audit row
    (`SpeakingPatientTurns`, next to its SHA-256; wiped with the row by the retention sweep after
    `LIVEVOICE__RETENTIONDAYS`). It is never stored for Gemini, whose "session id" is the ephemeral token, a bearer
    credential.
  - *When*: in the ai-worker (it needs the OpenAI key and URLs in the worker's env and does nothing, silently, without
    them), at the hard stop **whoever ended the role-play**: right after the sweeper finishes an Active role-play the
    client never ended and, in a second pass, for every Finished non-tutor role-play (the learner's `/end`, the exam
    clock, an exam cancel, or a hard-stop pass whose own hang-up failed) once it is past its hard stop and for 10 minutes
    after it (`HangUpHorizon`). So a browser that keeps its connection open after `/end` can still bill until the hard
    stop, and is cut there if the hang-up works and the worker is running. One attempt per role-play per worker process
    (an in-memory ledger; a failure is logged at Warning and not retried; a worker restart repeats the calls once, which
    is harmless because a 404 means already ended).
  - *Not covered*: Gemini (its token expiry bounds it), a session Cancelled by the free-sample rebind, and a role-play
    more than 10 minutes past its hard stop (for example after a long worker outage).
  - **Pending verification** that the endpoint ends an established GPT-Live session; until it is confirmed keep an OpenAI
    project spend limit as the financial backstop. Every hang-up logs its HTTP status in the ai-worker's log, so the
    first real run answers it: `OpenAI live session hang-up returned HTTP {status} for Speaking session {SessionId}`
    (Information for 2xx and 404, Warning otherwise) and, for a transport fault or timeout,
    `OpenAI live session hang-up failed for Speaking session {SessionId}: {ErrorType}` (Warning), one line per OpenAI
    session (three per role-play by default). Only the Speaking session id is logged, never the key, OpenAI's own session
    id or provider text. HTTP 200 means the endpoint works; every call answering 404 means it is probably wrong (a 404
    still counts as ended).
- **Sweeper** (`SpeakingExamAutoAdvanceWorker`, every 20 s). It runs only in the **ai-worker** container in production
  (the API slots run with `Ai__HostedWorkers__Enabled=false`; a Development or test API process runs it too), so
  finalisation can lag by up to about 2 minutes around a deploy (the worker stops with a 90 s grace); the wall-clock
  guards on mint and writes keep correctness in the meantime. `SweepOnceAsync` runs three independent passes, and a
  failure in one is logged at Error and never skips the others (each pass handles one batch of at most 500 rows per tick):
  1. the **exam pass**: the exam clock ends an exam card at `ActiveXStartedAt` + card seconds (`EndedAt` = that instant,
     grading enqueued), and hangs up nothing;
  2. the **hard-stop pass**: every Active non-tutor role-play past its hard stop, and at most **12 hours** past it
     (`RolePlaySweepHorizon`; older rows are left alone so a backlog cannot flood the grader, and their holds are
     refunded by the stale-hold sweep), is finished with a compare-and-swap Active to Finished: `EndedAt` = the deadline,
     `ElapsedSeconds` = the capped card seconds, the legacy attempt mirrored to Submitted, one `SpeakingRolePlayHardStopped`
     audit event. Only an **exam card** is then handed to grading (standalone practice: see "Abandoned practice" below);
     for a card this pass is only the backstop of the exam clock, which ends it at its deadline, before the hard stop.
     The OpenAI sessions of every role-play finished here are hung up straight away. A throw after the swap leaves the
     session Finished (never a hard-stop candidate again) but it is still hung up by the third pass in the same tick; its
     audit row and grading hand-off are not retried by the sweeper (the exam clock grades exam cards);
  3. the **hang-up pass** described under OpenAI.

  Log lines to grep (Error, one per failed pass): `Speaking exam auto-advance sweep failed`,
  `Speaking role-play hard-stop sweep failed` and `Speaking role-play provider hang-up sweep failed`. Warning, one per
  session: `Failed to hard-stop overdue Speaking role-play {SessionId}` and
  `Failed to hang up the provider sessions of ended Speaking role-play {SessionId}`. Warning, one per tick that finished
  any: `Speaking hard-stop sweep finished {Count} role-plays the client never ended.`
- **Late writes**: a turn, a transcript or a recording upload is accepted while the session is Active and until the write
  window closes: `TranscriptFlushGraceSeconds` (900 s; clamped 60-3600) after `EndedAt` for a Finished session, after the
  hard stop for one still Active. Turns and transcripts are also frozen (409 `live_voice_transcript_window_closed`) once
  a transcript exists **and** grading has taken it (an assessment exists, or the grading operation is leased,
  provider-succeeded or completed); with no transcript yet, a late flush is accepted because it is the first transcript.
  Recordings are only time-bounded, grading does not freeze them. A repeat upload of a recording the session already
  has is answered 409 `recording_already_received` even after the window closed (the duplicate check runs before the
  window check); a first upload after the window is 409 `live_voice_transcript_window_closed`. Guards are per child
  session, so Card A's late save still lands on Card A while the exam is on Card B, and a provider session of one card
  cannot write into the other.
- **Segment timing** is metadata: a transcript segment that spans more than 120 s has its `endMs` clamped to
  `startMs + 120000` instead of failing the save (there is no `live_voice_segment_duration_invalid` any more), because a
  rejected segment fails the whole save and a client that fails open on a 4xx would then drop the transcript. The other
  checks are unchanged (speaker, non-empty text, at most 4000 characters per text, at most 600 segments).
- **Client fail-open** (the exam will not show Card B until Card A's transcript is saved, so a permanent rejection must
  never strand the learner):
  - `stop()` for a live transcript resolves `true` when the transcript was saved, when nothing was said, and when the
    server will never accept it (any 4xx other than 401, 408 and 429, e.g. `live_voice_transcript_window_closed`). It
    resolves `false` only for a failure worth retrying (network, 5xx, 408, 429, and 401: an expired sign-in keeps the
    transcript and the provider session id, and the next `stop()` retries the same transcript). A per-turn row is
    advisory: if it fails to save it is only logged (`Live voice turn row not saved.`), never shown to the learner and
    never blocks the transcript, which `stop()` saves whole.
  - The recorder fallback's `stop()` (`useSpeakingSessionRecorder`) resolves `true` when the audio landed (`uploaded`)
    and, likewise, when the server permanently refused it (`rejected`: any 4xx other than 401, 408 and 429; label
    "Recording could not be saved", alert = the server's message or "The recording could not be saved."), so the practice
    page goes on to `/end`, `/submit` and `/ai-assess` and the exam advances. **Only 409 `recording_already_received`
    counts as uploaded**: a refused recording is never reported as received. 401, 408, 429, 5xx and network errors keep
    it on the device (`upload_failed`, label "Recording kept on this device", `stop()` false; "Retry upload" re-sends the
    same blob).
  - The exam and practice pages stop retrying after **3** failed *live-transcript* saves and move on. The mode that
    decides this is the card's own, fixed when it started: on the practice page the `liveVoiceAvailable` the session had
    when the page loaded; on the exam page the value of the DTO rendered in the commit that mounted the card's
    `ExamConversationPanel` (the panel latches it too), **never the latest 3 s poll**. A card that started live moves on
    after its 3rd failed save ("The live voice transcript could not be saved. Retrying before moving to the next card."
    until then). A card that started as a recording never moves on until its upload lands or is permanently refused
    ("Upload failed — Retry upload"), even if a later poll says live voice is back.
  - The exam page polls one at a time (an overlapping poll would run the previous card's save again).
  - The practice page starts its countdown from the card's own seconds (no hardcoded 5:00; the server's ceiling reaches
    it through `/clock` once speaking starts, and the page does not read `rolePlayEndsAt`), follows `/clock`, finishes at
    the wall-clock `hardStopAt` if a throttled tab ticks late, and treats 409 `speaking_session_invalid_state` from `/end`
    as "the server already ended it": it carries on to submit and grading.
- **Abandoned practice is not graded.** A standalone practice or free-sample role-play that hits the hard stop without the
  learner finishing is ended (`EndedAt` = the deadline, audited, its OpenAI sessions hung up) but **not graded**: its
  2-credit hold is left to the 24 h stale-hold refund sweep (`SpeakingCreditSettlement.SettleStaleHoldsAsync`, hourly, in
  the same worker) and a free-sample use stays retryable (owner rule: abandoned uses never count). Exam cards are graded
  as before, and a late but alive client is graded through its own `/ai-assess`. **Pending owner confirmation** of
  grading abandoned practice; to enable it, drop the `ExamSessionId` condition in
  `SpeakingSessionService.FinalizeAtHardStopAsync` (marked with a `ponytail` comment) and restore the grading assertions
  in the sweeper tests. Known cosmetic residue: the legacy attempt of such a session is marked Submitted although it is
  never graded.

## Card labels

An exam card is named by its slot, **Card A** and **Card B** (the server's `currentCardNumber` 1 | 2; the exam
sub-header, results page, tutor page and the V11 report use the same letters). The exam draws two random cards from the
profession's pool and the printed number on a source card is a per-source-set ordinal, so two cards can both print
"No. 4" (26 Sep). The printed number (`displayCardNumber`) is real data and stays on **practice** cards ("Role-Play
Card No. 7"); it never labels an exam card, and an unknown slot prints no letter rather than a guess.

- Learner exam: `slotLabel` on `SpeakingRoleCard` ("Role-Play Card A"); sub-header "Part 2 — Card A".
- Tutor exam view: the same `slotLabel` on `RoleplayerCard` ("Roleplayer Card A"), so tutor and candidate agree.
- V11 report header: "Card A" / "Card B" for slots `a` / `b`, "Full mock" for a combined report, no line for a standalone
  practice report (it printed "Card STANDALONE").
- The admin editor's "Printed card number" field now says it is the number on the source card and is shown on practice
  cards only. Nothing in the data changed: `displayCardNumber` is also the free-sample and corpus-harness ordering key.

## Production E2E

Workflow **Speaking live voice E2E (production)** (`.github/workflows/speaking-live-voice-prod-e2e.yml`), harness
`scripts/qa/speaking-live-voice-browser-e2e.mjs` with in-page probes in `scripts/qa/live-voice-browser-probes.mjs`, the
served-provider attribution and the pure verdict helpers in `scripts/qa/live-voice-served-provider.mjs` and the pure
saved-transcript judgement in `scripts/qa/live-voice-transcript-quality.mjs` (the workflow copies all four files into one
folder, the harness as `e2e.mjs`). Real Chromium, fake microphone playing a scripted candidate once (6 s lead-in, no loop), the
QA learner, the real provider. Runs queue one at a time (concurrency group), never dispatch during a deploy. How to dispatch,
the inputs added on 1 Oct 2026, the checks and the order of runs are under
[Dispatching and options](#dispatching-and-options) onwards.

- Inputs: `mode` practice | exam; `script`; `voice` piper | espeak; `voice_provider` (blank = the server's candidates,
  primary first; a provider set here asks the server for a pin, which it honours for a flagged QA learner only; a pinned run
  never fails over, so every check stays strict: [Dispatching and options](#dispatching-and-options)); `expected_primary`
  (blank = no assertion; fails an unpinned run if the first provider call goes elsewhere; **ignored when `voice_provider`
  pins a provider**: `metrics.expectedPrimaryIgnored` is true, a log line says so and `primaryProviderIsExpected` stays
  null); `fail_primary` (answers the expected primary's create call with a 503 in the browser and asserts the failover;
  needs `expected_primary` and a blank `voice_provider`. The faked answer is produced in the browser and **never reaches
  the API**, so only the client failover is exercised: the server's circuit, health model, provider-error capture and
  audit never see it); `speak_seconds` (practice; blank = the tape's last speech + 10 s, max 280 because the app auto-ends
  at 5:00). Scripts: `compare` (about 4 minutes of tape with room for the patient to finish, an early teach-back probe
  before any explanation and a late one after it) and `full-lactose` are written for the lactose practice card and are
  rejected in exam mode; `smoke` is the behaviour probe (barge-in, 25/45/60 s silences, teach-back before explaining);
  `short` and `generic` are minimal; `smoke-A` and `smoke-B` are `smoke` with a different closing sentence, to tell two runs
  apart.
- Artifacts (`live-voice-e2e`): `metrics.json` (usage, latency, barge-in / talk-over / silences, stability incl.
  WebSocket close codes and RTC states, `providerCalls` (every create call, failed ones included), `providerAttempts`
  (call, HTTP status, the app's error code, `hardStopAt`; never a body or token), `failoverObserved`, `hardStopAt`,
  `servedProvider` (`openai` | `gemini` | `mixed` for an exam whose cards were served by different providers | null; read
  from each connected card's panel `data-live-provider`, falling back to the 2xx create calls on a build without it),
  `expectedPrimaryIgnored`, per-card `Panel` (the provider the page reports serving) and `MicStreams` (track states, to
  see the microphone released between cards), checks),
  `timeline-events.json` (timed words incl. provider `start_ms`/`end_ms`), `saved-transcript-<session>.json` (the
  transcript API body the grader reads), `card-text.json`, `patient-audio-<n>.webm` (what was audible, both providers),
  screenshots. Since 1 Oct 2026 `metrics.json` also carries the credit, History, reload, grade-retry, wording and
  per-transcript quality records described below; the artifact is kept for 3 days.
- Attribution follows the provider that **served**, never "some OpenAI data-channel event exists": the transcript text,
  `providerErrors`, `sessionClosed`, the timeline words, `providerUsage` and `noSplitSentences` come from the serving
  provider(s), and a leg that failed over (its error event, its close, its 503) is ignored. `providerUsage` carries
  OpenAI's fields at the top level and, for a mixed exam, a nested `gemini` object (it used to be `{openai, gemini}`);
  analysis scripts should prefer `servedProvider` over inferring the provider from `providerCalls`. The run stops early
  with "no live voice provider could start the conversation" only on the app's own "live AI patient / voice provider could
  not start" message; a microphone ("Could not start the microphone") or exam-page ("Could not start the discussion")
  error ends as the generic "live voice did not connect within 45 s".
- Checks (a red check fails the run): `savedTranscriptsCaptured`, `noSplitSentences` (only when GPT-Live served),
  `staysInRole`, `noBrowserErrors`, `bargeInPatientStops`, `noPatientTalkOver`, `survivesSilences`,
  `primaryProviderIsExpected` (only with `expected_primary` and a blank `voice_provider`), `pinnedProviderServed` (only
  with `voice_provider`: every connected card's panel reports the pinned provider, exam mode too), `failoverAsRequested`
  (only with `fail_primary`: per card exactly one call to the failed primary then one to the other provider, plus the calls a
  recovery or a reload adds on the first card, and the page reports the other one serving), `recoveredAsRequested` (only with
  a drop or stall fault input, see [Fault injection](#fault-injection-mid-session-recovery)) and `cardLabelsBySlot` (exam:
  Card A's text says "Role-Play Card A", Card B's says "Role-Play Card B"). The checks added on 1 Oct 2026 are described under
  [Dispatching and options](#dispatching-and-options), [Transcript checks](#transcript-checks) and
  [Credits, History and wording checks](#credits-history-and-wording-checks). A 503 on one provider followed by a success on
  the other is a *failover*, not a browser error, but only in an unpinned run; usage and `noSplitSentences` follow the
  provider that actually served. Gemini patient speech is measured from playback timing, so its barge-in/talk-over numbers are
  not comparable with GPT-Live's pass/fail thresholds without care.
  `metrics.leakCheck` (exam) is a bag-of-words suspect list, not a check: read both transcripts (the cross-card checks under
  [Transcript checks](#transcript-checks) are the judged version). Teach-back is judged by reading the saved transcript, not by
  the harness.
- What to run after the 30 Sep change: an unpinned exam with `expected_primary` set to the current first choice; the same with
  `fail_primary` (practice and exam); a pinned run per provider for the like-for-like comparison (it needs the QA pin flag,
  [QA provider pin](#qa-provider-pin)). A `fail_primary` run proves the client failover only. The circuit, `hardStopAt` and
  the OpenAI hang-up need a real, unfaked run plus a read of `providerAttempts`, `GET /v1/admin/ai/live-voice/health` and the
  ai-worker's hang-up log lines. The 1 Oct 2026 changes have their own order of runs: [Run matrix](#run-matrix).

### Dispatching and options

Dispatch from a branch, never by merging: `gh workflow run speaking-live-voice-prod-e2e.yml --ref <branch> -f name=value ...`.
Every push to `main` redeploys production (new slot, the in-memory provider breaker is reset, in-flight grading is requeued), so
a harness-only change is never merged to run it. Dispatch one run at a time and wait for each to finish (GitHub keeps one
pending run per concurrency group). The first workflow step fails the run while `production-deploy.yml` has a run in progress or queued.
The artifact (QA transcripts, the patient's audio, screenshots) is kept for 3 days. `scripts/qa/live-voice-workflow.test.ts`
fails CI when a module the script imports is not copied, an environment variable the script reads is not wired, an input
description would break YAML (a colon-space in a plain scalar once made a push run fail with 0 jobs) or a script option has no
file.

The 13 inputs: `mode` (practice | exam), `card_id`, `script` (`short`, `full-lactose`, `generic`, `smoke`, `smoke-A`, `smoke-B`,
`compare`), `voice`, `speak_seconds`, `voice_provider`, `expected_primary`, `fail_primary`, `fault_drop_at_s`, `fault_stall_at_s`,
`fault_reload_at_s`, `verify_credits` and `grade_retry`. What is new on 1 Oct 2026:

- `voice_provider` asks the server to pin a provider. The server honours it only for a QA learner with an enabled feature flag
  `speaking_live_voice_pin:<learner user id>` ([QA provider pin](#qa-provider-pin)); for anyone else it answers 200,
  `pinned: false` and the normal order. The run fails fast, naming that flag (the learner id is read from the sign-in token),
  when the server ignored the pin, and `checks.pinHonoured` judges the server's answer on every preflight that carried
  `provider=` (a run that merely landed on the primary proves nothing). A pinned provider never fails over or recovers.
- `fail_primary` (needs `expected_primary` and a blank `voice_provider`) answers the primary's create call with a 503 in the
  browser; the API never sees it. It now combines with the fault inputs: the fault then hits the fallback provider
  (`checks.faultHitFallback`) and the create calls must be exactly [primary, secondary] per card plus what a recovery (the first
  restore retries the serving provider, the second one tries the other provider first) or a reload (primary, secondary again)
  adds on the first card (`failoverCallsOk`).
- `fault_drop_at_s`, `fault_stall_at_s` and `fault_reload_at_s`: [Fault injection](#fault-injection-mid-session-recovery).
- `verify_credits` and `grade_retry`: [Credits, History and wording checks](#credits-history-and-wording-checks).

### Fault injection (mid-session recovery)

Three optional inputs (blank = off, the run is unchanged) break the live link on purpose so the app's mid-session recovery and
its page-refresh behaviour can be measured. They hit only the first live conversation (practice, or exam Card A), N seconds after
the candidate microphone tape starts. One fault per run: `fault_drop_at_s` wins over `fault_stall_at_s`, which wins over
`fault_reload_at_s` (`metrics.fault.stallIgnored` and `reloadIgnored` record what lost).

- `fault_drop_at_s` (env `FAULT_DROP_AT_S`): kills the live provider connection from inside the page. Gemini: its WebSocket is
  closed (marked `injected` in `stability.wsClose`). OpenAI: the `oai-events` data channel is closed, because its `close` event
  is what the app listens to (`peer.close()` alone fires nothing locally).
- `fault_stall_at_s` (env `FAULT_STALL_AT_S`): the provider goes silent. Every server-to-client message (Gemini frames, OpenAI
  data-channel events) is swallowed before the app and the recording probes see it, and the patient's WebRTC audio is silenced.
  It ends by itself when the app builds a NEW transport, so the recovered session behaves normally. It exercises the app's
  silence watchdog, so the candidate must still speak after the stall began. Gemini's wire-level records (`transcript.json`,
  the timeline words, usage, `patient-audio-raw.wav`) still include what the stall hid from the app; what the learner could
  hear is the speech spans and `patient-audio-<n>.webm`.
- `fault_reload_at_s` (env `FAULT_RELOAD_AT_S`): the learner presses F5. The harness reloads the page, presses "Start speaking"
  (or "Retry connection", up to three presses) when the panel does not reconnect by itself, and records `metrics.reload` =
  `{ resumed, presses, autoStarted, resumeMs, providerAfter, preReloadRecall }`. `checks.reloadResumed` (the panel came back
  live and did not end in its error state) and `checks.reloadKeepsTranscript` (the words said before the refresh, both
  speakers, at least 80% of the provider's words, are in the saved transcript: [Refresh behaviour](#refresh-behaviour)) judge
  it. The tape restarts in the new document, so a reload run's latency is not comparable and the tape comparisons (Q4, Q5, Q10,
  Q11) are skipped for that card.
- Combinations. A drop or a stall needs a blank `voice_provider` (a pinned provider never recovers, so the fault would only
  kill the session): refused by a workflow guard step and again by the harness, before any provider is billed. A reload may pin
  one. All three work with `fail_primary`. The fault time must be before the conversation ends (`speak_seconds`, or 280 in exam
  mode). Use `short` or `generic`: `smoke`'s 25-60 s silences would turn `survivesSilences` red for the wrong reason.
- Metrics: `metrics.fault` = `{ kind: 'drop' | 'stall' | 'reload' | null, atSeconds, firedAt (epoch ms, null = it never fired),
  provider (the transport hit), recoveredAt (the first provider create call after the fault, once the panel reports a
  recovery), swallowed (stall: messages hidden from the app), stallIgnored, reloadIgnored, error (only when no live transport
  was found) }`.
  `metrics.recoveries` = the panel's `data-live-recoveries` (exam: the total, with `recoveriesA` / `recoveriesB`; 0 when the
  attribute is absent, null when the panel was never read). It is recorded on every run, fault or not.
- Check `recoveredAsRequested` (null without a drop or stall fault, and null for a reload, which has no recovery: see
  `metrics.reload`): the fault fired, the faulted card's panel reports at least 1 recovery, the patient produced a speech span or
  a transcript delta after the recovery session was asked for, and the panel did not show its error alert at its last live
  reading. The "Reconnecting the patient…" text is not required. It does not look at the saved transcript (the 1 Oct recovery
  runs passed it while their saved transcripts were corrupted): the [transcript checks](#transcript-checks) do.

### Transcript checks

Every saved transcript (per card in an exam) is judged the way the grader reads it: `metrics.savedTranscripts[id].quality`,
summarised in `checks.transcriptQuality`. The earlier checks could not see a corrupted transcript (the three 1 Oct recovery runs
passed all of them, see [Transcript time base](#transcript-time-base)). The rules, calibrated on 21 real production transcripts
from the 1 Oct runs:

| | Rule |
| --- | --- |
| Q1 | no two segments of one speaker less than 10 s apart (after a longer silence a same-speaker neighbour is legitimate: [Pause rule](#pause-rule)) |
| Q2 | segment starts non-decreasing (1.5 s tolerance) |
| Q3 | opposite-speaker overlap at most 1.5 s |
| Q4 | candidate segments at least 0.75 x the scripted lines played |
| Q5 | the lines that start a segment sit at one constant offset from the tape (spread at most 2 s), and at least 70% of the lines start one |
| Q6 | at most 10% zero-length candidate segments |
| Q7 | all times within 0..330 s |
| Q8 | no fused words |
| Q9 | no segment over 30 s |
| Q10 | candidate word error rate at most 10% |
| Q11 | the script's closing line said exactly once |

Lines the run itself hid (a stalled or dropped link) are excluded. The unit tests replay two real artifacts: the clean OpenAI
Card A of E1 passes everything except Q9 (a 54 s segment that hid 45 s of silence, which the [pause rule](#pause-rule) now
splits) and the corrupted recovery run F1 fails exactly Q4, Q5 (a 59.9 s jump) and Q8 ("Doctor.Well").

`metrics.savedTranscripts[id].wire` compares the saved text with what the provider sent in that card's window
(`checks.transcriptsMatchWire`: multiset recall and precision at least 0.95 per speaker), the labels
(`checks.candidateLabelsAreTheTape`: the saved candidate words are the tape's, the saved patient words are not) and whether every
saved patient segment is the patient's own words of this card (`checks.noCrossCardLeak`, plus at most one long patient sentence saved
identically in both cards: the TEACH-BACK line "you haven't told me what it is yet" can legitimately come back, a card saved twice repeats many). `checks.savedProviderMatchesServed`: the server's transcript provider (`realtime-openai` or
`realtime-gemini`) is the one the panel showed. Because one fake-microphone file plays the same tape in both cards of an exam, the
two cards cannot carry different sentinel lines; use `smoke-A` and `smoke-B` for two separate runs.

### Credits, History and wording checks

- `verify_credits` reads `GET /v1/me/ai-package-credits?pageSize=200` through the page's own bearer (not `/v1/me/ai/credits`, the
  old token ledger) before anything is spent and refuses to start (nothing billed) when the account is funded another way
  (unlimited Speaking, a mock exam unit for an exam, a free-sample practice card), cannot fund the run or has credits expiring
  within two days. It then reads the balance when each card's hold exists (exam: -2 after Card A, -4 in total after Card B;
  practice: -2), after grading and at the end. `checks.creditsDeductedOnce`: exactly the expected `GradingDeduct` rows
  (`exam:<examId>:cardA` and `:cardB`, or `practice:<sessionId>`), 2 credits each, no refund or mock row, and the balance moved by
  exactly 4 (exam) or 2 (practice) ([state-machines.md](state-machines.md#credits-ai-exam-and-practice-card)). The proof assumes
  the QA learner has no other credit activity during the run.
- It then opens `/submissions` and judges History (`checks.historyListsExam`, [api-surface.md](api-surface.md#history-notes)): one
  row (exam: titled "Full Speaking Mock", `attemptId` and `contentRef` = the exam id, route `/speaking/exam/<id>/results`, status
  `completed`, `creditsUsed` 4; practice: route `/speaking/sessions/<id>/results`, `creditsUsed` 2), a `resultLabel` "N/500" equal to
  the score the results page showed and shown on the page, and no Speaking attempt of the run in Past Evidence
  (`/v1/submissions`).
- `grade_retry` asks for the grade once more per card after grading (`POST /ai-assess`, at most two a minute) and expects 200 or
  202, the same `assessmentId` and an unchanged ledger (`checks.gradeRetryIdempotent`; `metrics.gradeRetry`, and
  `metrics.aiAssessCalls` counts the calls the page made itself).
- `checks.inputKindLiveVoice`: `GET .../results` says `live_voice` for every session. `checks.resultsWordingHonest`: no results
  page says "recording" outside the Transcript tab or shows an audio player, each says it was a live conversation, and the exam
  results show a readable band with the advisory sentence, never a raw code such as `exam_ready` ([Results wording by input
  kind](#results-wording-by-input-kind)). It flags any "recording" outside the Transcript tab, including text the AI grader wrote
  (the grader note reduces but cannot guarantee that), and `metrics.resultsWording.problems` quotes the offending line.
  `checks.noProviderNamesInUi`: the live screens never name OpenAI, Gemini or GPT-Live.
- `metrics.softChecks.gradedByClaude` reports the grader from the page's own answers and never fails the run.
  `metrics.conversation.latency` now has `p95Ms` (nearest rank) and `samplesMs` (the raw values, so several runs can be pooled for
  a percentile; a single run has about 11 candidate lines per card) next to `samples`, `medianMs` and `p90Ms`. Gemini usage frames
  carry the socket index, so a recovery run can be costed per session.

### Run matrix

For the 1 Oct 2026 changes: dispatch one run at a time, from a branch, against the final deployed build, adding the flags below to
`gh workflow run speaking-live-voice-prod-e2e.yml --ref <branch>`. The new checks have never run, so a first red can be a harness
bug rather than a product defect: start with RUN 1 as a rehearsal.

- **RUN 0** (free): the deploy finished and the live build verified; admin `GET /v1/admin/ai/live-voice/health` shows
  `candidateOrder` `[openai, gemini]` with both circuits closed (note the baseline attempts and failures); the Gemini key is on a
  paid project; the QA learner has no mock exam units, no unlimited Speaking, no unused free sample and at least 8 AI credits;
  the pin flag exists if a pinned run is wanted; the repository is public for the run window if the owner's CI rule requires it;
  nobody else is testing.
- **RUN 1** (rehearsal, Gemini fallback, practice, about 6 min, -2 credits): `-f mode=practice -f script=short -f voice=piper -f expected_primary=openai -f fail_primary=true -f verify_credits=true -f grade_retry=true`. Expect `providerCalls` `[openai/offer, gemini/token]`, `failoverObserved`, the role-play panel on `gemini` with `failedOver`, `creditsDeductedOnce`, `gradeRetryIdempotent`, `historyListsExam`, `transcriptsMatchWire`, `savedProviderMatchesServed` (`realtime-gemini`) and `recoveredAsRequested` null.
- **RUN 2** (Gemini drop recovery): RUN 1 plus `-f fault_drop_at_s=40 -f speak_seconds=150`. Expect `providerCalls` `[openai/offer, gemini/token, gemini/token]`, 1 recovery, `faultHitFallback`, `recoveredAsRequested` and `failoverAsRequested`.
- **RUN 3** (Gemini stall recovery): RUN 1 plus `-f fault_stall_at_s=40 -f speak_seconds=200` (no `fault_drop_at_s`). Expect `fault.swallowed` above 0 and a restore about 25-40 s after the stall.
- **RUN 4** (the main proof: Gemini serves a real two-card exam, recovery on Card A; about 17 min plus any wait for a runner, -4 credits): `-f mode=exam -f script=generic -f voice=piper -f expected_primary=openai -f fail_primary=true -f fault_drop_at_s=45 -f verify_credits=true -f grade_retry=true`. Expect `providerCalls` `[openai/offer, gemini/token, gemini/token, openai/offer, gemini/token]`, both panels on `gemini` with `failedOver`, credits -2 after the Card A hold, -4 after the Card B hold, -4 after grading and at the end with exactly two ledger rows, and every transcript check green.
- **RUN 5** (refresh on real OpenAI; about 16 min, -4 credits): `-f mode=exam -f script=generic -f voice=piper -f expected_primary=openai -f fault_reload_at_s=60 -f verify_credits=true`. Expect three `openai/offer` calls, `metrics.reload` (`autoStarted` true or false, `resumeMs`), `reloadResumed`, `reloadKeepsTranscript` and credits -4 once (a refresh costs no second hold).
- **RUN 6** (clean acceptance on the final SHA; about 16 min, -4 credits): `-f mode=exam -f script=generic -f voice=piper -f expected_primary=openai -f verify_credits=true -f grade_retry=true`. Optionally repeat with `-f script=smoke-A` and then `smoke-B`. Pool `conversation.latency.samplesMs` of RUN 5 and 6 for the p95.
- **Pinned comparison run:** add `-f voice_provider=gemini` (practice) only when the pin flag exists; otherwise the run fails fast
  naming the flag. Two faults in one run are not supported.
- **After every run** (admin): `GET /v1/admin/ai/live-voice/health` (OpenAI failures and attempts unchanged for RUN 1-4; Gemini
  attempts up by the number of Gemini mints; both circuits closed; reset with `POST /v1/admin/ai/live-voice/<provider>/reset` if a
  run tripped one), `GET /v1/admin/ai/operations?featureCode=speaking.grade` (one Completed operation per graded card) and
  `GET /v1/admin/ai-package-credits/<qaUserId>` (no `GradingDeduct` reference with more than one row).

**Results of the first matrix (1 Oct 2026, production build 679e29cdc, the shared QA learner, Claude Max sidecar grading every
card).** Each run is the workflow run id; "wall" includes the wait for a GitHub runner (RUN 4 waited 7 min because other CI jobs
held every hosted runner). After every run the admin health showed both circuits closed and no `GradingDeduct` reference with
more than one row.

- **RUN 6** (36926334784, exam, OpenAI, wall 16 min): credits 42 -> 40 -> 38 -> 38 (exactly 4, once, also after a repeated
  grade), History, wording and grade retry green, both transcripts 22 segments, no split, quality green. Latency median 1588 ms,
  p90 2138, p95 2270 (n 22); billed 298 + 299 s = 597 s. Red only `bargeInPatientStops` (the patient needed 2245 ms to stop; the
  limit is 2000) and `noPatientTalkOver` (775 ms): behaviour limits of the model, not defects.
- **RUN 5** (36928848422, exam, OpenAI, refresh at 60 s, wall 15 min): **every check green.** The page came back and restarted by
  itself in 5.7 s, credits 36 -> 34 -> 32 -> 32, transcripts of 30 and 22 segments, no split. Median 1767 ms, p95 2094 (n 26);
  billed 237 s (the session after the refresh) + 300 s. The 3 console errors are the `placement/status` 404 probe
  (`placement_disabled`), which the harness treats as benign.
- **RUN 1** (36928209448, practice, OpenAI refused by the harness, Gemini serves, wall 5.5 min): credits -2 once, History,
  wording and grade retry green. Red `failoverAsRequested` (the recovery below was a third provider call) and `transcriptQuality`
  Q5/Q9/Q10, which are one event: **Gemini went silent by itself** after 74 s, the stall detector restored it 26 s after the first
  unanswered sentence, but the three sentences spoken meanwhile were never transcribed (the candidate text differs from the
  played script by 25.9% of its words) and one candidate segment spans 30 s. Median 2616 ms (n 6).
- **RUN 4** (36930552283, exam, Gemini, drop at 45 s of Card A, wall 23 min): provider calls exactly `[openai/offer,
  gemini/token, gemini/token, openai/offer, gemini/token]`, the drop restored in 0.3 s, credits 32 -> 30 -> 28 -> 28, both cards
  graded, History, wording and grade retry green, Card A transcript clean (22 segments, 6.8% of words differ from the script).
  Red `transcriptQuality` Q2/Q3 and `survivesSilences`, one event on Card B: **Gemini answered the first line of the card 12.6 s
  late**, so its short reply (25.0 s) was saved ahead of the candidate's second line (20.9 s). Median 2446 ms, p90 3504, p95
  4030 (n 21); the patient stopped 0.1-0.5 s after a barge-in.
- **RUN 2** (36933000359, practice, Gemini, drop at 40 s, wall 6 min): restored in 0.0 s (a new session was minted 42 ms
  after the drop), credits -2 once, History, wording and grade retry green. Red `transcriptQuality` Q5 only: the sentence that was
  being answered when the link dropped never got its reply (the replayed history leaves it unanswered), so two candidate lines
  share one segment and the check reads the shared start as a timeline jump of 13 s. Median 2553 ms (n 7).
- **RUN 3** (36933651577, practice, Gemini, stall injected at 40 s, wall 7 min): 34 provider frames were swallowed and the app
  restored the patient 8.0 s after the stall began, credits -2 once, History, wording and grade retry green. Red
  `noPatientTalkOver` (750 ms) and `transcriptQuality` Q2/Q3/Q9: a quick patient reply was saved ahead of the candidate line it
  answered (Gemini segments are ordered by arrival, see [Known open items](#known-open-items)) and three candidate lines share one
  33 s segment because the replies in between were swallowed. Median 2537 ms (n 5).

All six runs together: both admin circuits stayed closed, the QA learner's Speaking pool went 42 -> 24 (4 + 2 + 4 + 4 + 2 + 2),
71 distinct `GradingDeduct` references and none twice, and every card was graded by `writing-claude-sub`.

What the matrix did **not** run: a pinned run (the flag was never created), an OpenAI drop or stall after the time base change
(the last ones, F1-F3, predate it), a Gemini run after the 2 Oct start-order fix, and a real provider outage.

## QA provider pin

**Pending: not yet verified in production** (the verification steps are at the end of this section).

- **What it is.** `?voiceProvider=openai|gemini` on the practice or exam page (and the workflow input `voice_provider`, see
  [Dispatching and options](#dispatching-and-options)) is only a **request**.
  `GET /v1/speaking/realtime/sessions/{id}/preflight?provider=` honours it only when the signed-in learner has an **enabled**
  `FeatureFlags` row whose key is exactly `speaking_live_voice_pin:<learner user id>` (key format: `LiveVoiceService.PinFlagKey`;
  the id is the `NameIdentifier` claim of the sign-in token, which equals `SpeakingSession.UserId` and the learner's id in the
  `/admin/users/<id>` address; the key is case-sensitive and only `Enabled` is read, a rollout percentage is ignored). The flag is
  read only after the session is proven the caller's and only when a provider is requested, so an ordinary preflight costs no
  extra query.
- **Enable** (owner; do it **before** the deploy, because a row is inert on the old build): Admin > Feature Flags > Create Flag >
  Name `Live voice QA pin - <label>`, Key `speaking_live_voice_pin:<learner user id>`, Type Operational, Owner QA, Description
  (who authorised it and why), Initial state Enabled > Save. It takes effect on the next preflight, with no deploy, and is audited
  and notified. Kill switch: press Disable. Anyone holding the `feature_flags` admin permission can authorise an account this way
  (an accepted trade-off).
- **Unauthorised caller.** HTTP 200, the normal order and `pinned: false`, never a 403 or 503: a candidate who merely follows a
  link carrying the parameter must keep failover and recovery. Even `?provider=bogus` is ignored. One Warning per request (a
  candidate who keeps a `?voiceProvider=` URL produces one or two per card):
  `Live voice provider pin ignored for user <id> (requested <value>): the account has no enabled QA pin flag.` The requested
  value is logged only as a short plain token, else as `(unsupported value)`. **It fails closed:** if the flag cannot be read,
  the pin is not honoured and the Warning ends `the QA pin flag could not be read (<ExceptionType>).` instead (the exception
  type, never its message).
- **Honoured pin.** One candidate, no failover, circuit bypassed (the provider must still be configured and verified; an unknown
  provider is a 503 `live_voice_provider_not_configured`), `pinned: true`, the disclosure names only that provider, and a pinned
  run never recovers mid-session.
- **Browser side.** The page value is sent as `?provider=` on the preflight and nothing more. The browser trusts only the
  preflight's `pinned`: a pinned run makes one attempt with the named provider and never fails over or recovers; when the server
  answers `pinned: false` (any account without the flag) the normal candidate order, failover and mid-session recovery apply even
  though the page asked for one provider.
- **Accepted residual.** The create routes (`POST .../openai/offer`, `POST .../gemini/token`) are not gated, so a signed-in
  candidate with devtools can still choose between two healthy providers. Bounded: both providers get the same persona
  instructions, the grading and credit path is the same, the saved transcript's provider is the server-recorded one, and at most
  3 provider sessions are allowed per active role-play. Refusing an open-circuit provider on the create routes for non-QA callers
  was deliberately not done (it reverses the note on `EnsureProviderConfigured`).
- **Verify on production after the deploy** (needs a learner token). As an ordinary learner, with any AI-mode session id,
  `GET /v1/speaking/realtime/sessions/{id}/preflight?provider=gemini` must answer 200 with `pinned: false`,
  `provider: 'openai'`, `candidates: ['openai','gemini']` and a disclosure naming both providers with "may switch to"; repeat with
  `?provider=openai` and `?provider=bogus` (200, not 503). As the flagged QA learner the same call must answer `pinned: true`,
  `provider: 'gemini'`, `candidates: ['gemini']`; press Disable on the flag and repeat: `pinned: false`. As the ordinary learner,
  open a card with `?voiceProvider=gemini` appended: `data-live-provider` must be the primary and there must be no
  `data-live-failover`.

## Not implemented

- **No active canary.** Health is fed only by real learner session creations (plus the `/models` catalog probe), so a
  provider that went bad while idle stays "closed" until a learner hits it; the cost is one fast failed attempt, failed
  over transparently.
- **Client connect failures never feed the circuit.** A leg that never went live after a successful create call (ICE or
  DTLS failure, the 15 s deadline, a socket error) is handled by the browser's failover only; the browser reports
  nothing back and the server never sees it. Only the server's own create-call outcomes count.
- **The catalog probe and the circuit live in the API slots only.** The probe does not run in the ai-worker, and the
  circuit state is per API process (a restart or a slot swap clears it).
- **Gemini sessions are never closed server-side**, and the OpenAI hang-up is unverified against the real provider (see
  [Hard duration cap](#hard-duration-cap)).

## Known open items

- **First full production matrix, 1 Oct 2026** (build 679e29cdc, results in the [Run matrix](#run-matrix)). The OpenAI exams
  (RUN 5, RUN 6) passed every product check; RUN 6's two red checks are the model's behaviour (the patient needed 2245 ms to stop
  after a barge-in against a 2000 ms limit, and talked over the candidate once for 775 ms). Gemini, the fallback, failed over,
  restored a dropped link in 0.0-0.3 s, charged credits exactly once and was graded, but showed three limits that are provider
  behaviour or by design and are **not fixed**: (1) a Gemini link can go silent by itself (RUN 1, 74 s in): the stall detector
  restores it about 26 s after the first unanswered sentence, but the sentences spoken meanwhile are never transcribed (words
  spoken while a link is down are lost); (2) the sentence in flight when any link drops gets no reply and the candidate has to
  speak again (RUN 2), after which two consecutive candidate lines share one transcript segment; (3) a Gemini patient can be 12 s
  late with the first reply of a card (RUN 4 Card B), and a quick reply that starts while the candidate is still talking (RUN 3)
  has the same effect: Gemini transcribes the candidate ~1.5 s late, its segments are ordered by arrival and only OpenAI has the
  late-fragment rule, so the patient's text was saved ahead of the candidate line it answers (2 of the 4 Gemini runs). **Fixed in
  code on 2 Oct 2026** (`inOrderOfStart`: the transcript is saved in a stable order of start time; unit-tested, not yet re-run on
  production). Measured, Gemini's median reply is 0.8 s slower than OpenAI's (2.4-2.6 s
  against 1.6-1.8 s, p95 4.0 s against 2.1-2.3 s) and it stops talking sooner when interrupted (0.1-0.5 s against 1.2-2.2 s).
  The harness checks `transcriptQuality` Q5 and `failoverAsRequested` read these Gemini events as failures; excluding a spontaneous
  stall window and a lost reply is a harness follow-up. The matrix also showed every card's grader note stating that confidence is
  low because the classic live-role-play grader receives transcript text only for linguistic criteria; consented microphone
  clips linked to candidate transcript spans are used by the separate v1.1 acoustic assessment when its evidence gates pass.
- Admin `GET /v1/admin/ai/operations?featureCode=speaking.grade&state=Leased` listed 82 operations without a resource id that stay
  Leased (their lease is refreshed every 30-60 min; the oldest was created on 30 Sep 19:43). They did not block or double-charge
  any grade in the matrix (credits exact, one `GradingDeduct` row per card); the cause (a first gateway attempt that never reaches
  a terminal state) is not investigated.
- Verified in production on 30 Sep - 1 Oct (OpenAI primary, builds 74ca2607b and 7e9a4a58a): the provider order
  `[openai, gemini]`, the browser failover (a faked OpenAI 503 served by Gemini, run E4), mid-session recovery (a dropped data
  channel and a stalled provider, runs F1-F3), the card labels by slot and `hardStopAt` on the session response. Still
  unverified: the OpenAI hang-up at the hard stop (read the ai-worker hang-up log lines), the server circuit with a REAL
  provider outage (the harness's faked 503 never reaches it) and the client fail-open rules.
- The exact cause of the 30 Sep OpenAI 429 (quota, rate limit or project budget) is unread: the provider's body used to be
  discarded. The provider's error code and, for a 429 or a 5xx from the vendor host, its redacted message are now in the
  failure log line, and the class, status and code are on the admin health endpoint; read it there.
- The 26 Sep two-card mock ended red; only 1 of 4 exam runs that completed both cards was ever graded (bugs fixed by #259/#264).
- Gemini vs OpenAI: the 1 Oct matrix gives graded runs of both (above), but voice quality, realism and card adherence are
  listening judgements that no harness reads (the blind A/B listening set is the owner's input). Claude grading is $0 marginal on
  the Max subscription sidecar; the Anthropic API route still has no credit and is only the fallback.
- The Rules and consent screen says short clips are captured from the learner microphone during detected speech, the app
  does not make a full-session recording or directly record provider playback, and browser echo cancellation cannot guarantee
  isolation. The results copy was aligned to the same limit ([Results wording by input kind](#results-wording-by-input-kind));
  this clip path is implemented but not production-verified.
- Recovery is best-effort (two restores per card, none for a run the server pinned). Measured in production on 1 Oct 2026
  (OpenAI, build 74ca2607b, compare script, unpinned): a dropped data channel at 60 s had a new session offered 0.6 s later and
  connected 1.7 s after the drop (2 OpenAI offers, 308/500), but the saved transcript was corrupted: 16 segments instead of the
  24 of a correct timeline, five scripted lines fused into one candidate segment, while every check of that run was green (the
  time base defect, fixed in code and pending verification: [Transcript time base](#transcript-time-base)); a stalled provider
  was restored 94 s after the stall (the sentence-clock defect above, fixed since). Gemini recovery runs in production through
  `fail_primary` plus `fault_drop_at_s` or `fault_stall_at_s` ([Run matrix](#run-matrix), RUN 2-4); record the measured restore
  times here after the first runs. The harness still cannot prove a real server-side outage, a circuit that is actually open
  (`candidates = [gemini]`), provider-side orphan billing, native apps or voice quality, and it does not judge a card whose
  second restore switched providers mid-card (the wire words are attributed by the panel's first-connect provider). Hidden
  information is prompt-only.
- A signed-in candidate can still choose between two healthy providers by calling a create route directly: the accepted residual
  of the pin gate, bounded as described under [QA provider pin](#qa-provider-pin).
- A closed tab, a crash, or a reload that is never followed by Start speaking saves no transcript (the browser's copy needs a live
  provider session to be saved, [Refresh behaviour](#refresh-behaviour)). The grader then waits (it retries every minute for up
  to an hour) and fails the card, "Try grading again" cannot succeed, and the exam results page's slow-grading notice still says
  the role-plays are saved. Building the transcript from the saved turn rows on the server is an **owner decision**.
- No server-side completeness check: a truncated transcript is graded and scored normally (a Gemini exam Card A saved as 2
  segments and 62 words was graded 141/500). The policy (a low-confidence banner, a free retry or a refusal) is an **owner
  decision**.
- A provider session that was created but never connected (failover after a successful create, or a dead tab) cannot be
  closed by the browser. For OpenAI the server keeps the raw session id in the `live_session` audit row (wiped by the
  retention sweep, `LIVEVOICE__RETENTIONDAYS`, 30 by default) and hangs it up at the hard stop
  ([Hard duration cap](#hard-duration-cap)), so an orphan can bill until then, or longer for a role-play more than 10
  minutes past its hard stop or one Cancelled by the free-sample rebind. Gemini tokens are stored as a hash only (they
  are bearer credentials), are never closed server-side and expire at the latest at the hard stop + 15 s.
- Silent failover changes the data processor under generic consent copy ("the live voice provider"); the server's
  `disclosure` names every candidate but the UI does not show it: owner/legal decision
  ([Consent and disclosure](#consent-and-disclosure)).
- When every provider fails the learner can only retry; a recorder fallback for that case would change what is graded and
  needs the owner's approval.
- Abandoned standalone practice is finished but not graded (pending owner confirmation, see [Hard duration
  cap](#hard-duration-cap)). After a recorder upload the server permanently refused, the results page still says
  "Grading your role-play…"; a non-retryable "No recording was received" state would make that honest (product follow-up).
- `LIVEVOICE__GEMINITOKENLIFETIMESECONDS` is dead configuration, kept only so existing env files still bind.
- OpenAI over a UDP-blocked network (hospital Wi-Fi) has no TURN/STUN configured and now fails after the 15 s deadline; it
  matters when OpenAI is the fallback.
- Card A and Card B can be served by different providers after a circuit change (per-exam provider analysis is mixed).
- The deploy workflow asserts no build identity; only the local `scripts/ship/watch-deploy.ps1` does (`LIVE_SHA_OK`).
- Scheduled Speaking Playwright and accessibility suites: last green 7-8 Sep; the failures on 28 Sep were real
  (auth refresh 400, target-size); the 29 Sep runs were Actions billing blocks.
