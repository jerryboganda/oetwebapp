# Speaking live voice agent (AI patient)

Record of the September 2026 rollout and testing. Written 2026-09-30 from the code, GitHub Actions logs and the
run artifacts of the 25-26 Sep production E2E runs; extended the same day with the close-out of the 30 Sep incident
(OpenAI session creation answered 429, so learners saw "could not start this conversation" and there was no
fallback). **Items marked "pending" have not been verified in production.** Everything under "Provider failover",
"Provider health", "Hard duration cap" and "Card labels" is new in that close-out and is pending until the production
E2E below has been run against it.

## Providers

| | OpenAI GPT-Live (production default) | Gemini Live |
| --- | --- | --- |
| Model | `gpt-live-1`, WebRTC via `POST /v1/live/sessions` | `models/gemini-3.8-live`, browser WebSocket with an ephemeral token |
| First choice | `LIVEVOICE__PRIMARYPROVIDER=openai` (code default, `appsettings.json`, compose) while both providers are healthy | `LIVEVOICE__PRIMARYPROVIDER=gemini`; or `?voiceProvider=gemini` on the practice/exam page, which **pins** the run to that provider (no failover). Any learner can pin if the provider is configured and verified; there is no role or flag gate |
| Billing (published) | $0.05 per minute of session, billed per second | Tokens: $0.75/M text in, $3.00/M audio in, $4.50/M text out, $12.00/M audio out |
| Measured usage | provider `session.closed` -> `usage.seconds`: 286 s per 5-minute role play, 298 s per exam card | `usageMetadata` per turn |

Cost caveat: only the seconds and token counts are measured. OpenAI $0.238 (286 s) and $0.497 (596 s, two cards) are
billed seconds x $0.05/min. Gemini about $0.27-0.35 per 5 minutes assumes Google re-bills the whole audio context each turn
(about $0.06 if it does not). None of these is an invoice figure. Claude grading cost has never been measured.

Switch the default: set `LIVEVOICE__PRIMARYPROVIDER` (`openai` | `gemini`) in the VPS env and recreate the API slot.
Keys: `LIVEVOICE__OPENAI*`, `LIVEVOICE__GEMINI*`. Values are never logged.

Default history: `openai` has been the code/compose default since 22 Sep 2026 (`74fdadd80`); on 26 Sep the VPS env was
switched from Gemini to OpenAI by hand (backup `.env.production.bak-2026-09-26-primary-openai`); both API slots ran
`openai` on 30 Sep 04:08 UTC.

### Operational note: temporary Gemini default (needs an owner-approved VPS env edit)

The owner wants Gemini tried first for now. That is a change to the production env, not a code change, and it needs the
owner's approval (AGENTS.md: agents do not edit `.env*`; the 26 Sep switch is the precedent):

- Edit `LIVEVOICE__PRIMARYPROVIDER=gemini` **in place** in `/opt/oetwebapp/.env.production` after a `cp -p` backup. Never
  append a second line and never use an alias (`gemini-live`): `validate-production-env.sh` rejects both and would fail
  every later deploy, hotfixes included. The compose default (`docker-compose.production.yml`) and the code default can
  stay `openai`.
- Only containers created afterwards see the new value. Recreate the **inactive** API slot from the live slot's images,
  gate on `/health/ready` and the `Live voice provider probe verified` log lines, flip the router, then read the
  router's `ACTIVE_SLOT` (`docker exec oet-api printenv ACTIVE_SLOT`): the flip has silently not taken effect three times
  (10, 26, 27 Sep). An in-place recreate of the active slot costs 30-60 s of 502s and kills in-flight `POST /ai-assess`
  grades. The ai-worker does not need a restart for this key.
- It is **not needed to restore service**: with failover, an OpenAI 429 is already routed around and the learner lands
  on Gemini after about 2-3 s. The flip only makes Gemini the first attempt.
- Check the billing project of the key in `LIVEVOICE__GEMINIAPIKEY` first. A free-tier key would answer real learners
  with 429s, and failover would then run the other way (Gemini fails, OpenAI serves).
- Prove it with the production E2E (`voice_provider` blank, `expected_primary=gemini`): `metrics.json` must show
  `providerCalls[0] == "gemini/token"` and the `primaryProviderIsExpected` check green.

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
- No reconnect: a connection that drops mid-conversation ends the patient for that card. A provider that cannot *start*
  is replaced by the other one automatically ([Provider failover](#provider-failover)). The recorder fallback is used
  only while **no** provider is usable (`liveVoiceAvailable` = at least one candidate; it was primary-only before).
- The app does not record billed seconds or live-voice cost; live voice sits outside the AI budget caps. Duration is
  bounded by the [hard duration cap](#hard-duration-cap).
- Grader: default route `claude-sonnet-5`; the gateway forces effort `max`, adaptive thinking and `max_tokens` 128000 for
  `speaking.grade` (the production route row was not re-read). A full 5-minute role play took about 8-9 minutes to grade
  in the 26 Sep runs (PR #259 quotes 11-12). Deploys stop the AI worker with a 90 s grace, so an in-flight grade is
  requeued and restarts from zero (derived from the code; not observed in production).

## Provider failover

Failover is executed by the browser and decided by the server, because the OpenAI leg needs a browser-made SDP offer
and the Gemini leg needs the browser to open the WebSocket: the server cannot finish a cross-protocol failover alone.

**Contract** (`lib/api/speaking-live-voice.ts`; all additions are optional, so an old client or an old server keeps working):

- `GET /v1/speaking/realtime/sessions/{id}/preflight[?provider=]` returns its usual fields plus `candidates`
  (providers in the order to try, healthiest first) and `pinned` (true when the caller forced a provider). No
  `candidates` (old server) means one attempt with `provider`.
- `POST .../openai/offer` and `POST .../gemini/token` keep their shapes and add `hardStopAt` (ISO time).
- A provider failure is **HTTP 503** with `retryable: false` and one of `live_voice_provider_unavailable`,
  `_timeout`, `_invalid_response`, `_unverified`, `_not_configured`. That is the signal to try the next candidate.
- Any 4xx is a definite answer about this session and is never failed over.

**Client loop** (`useSpeakingRealtimeVoice`):

1. Preflight gives the plan (`planProviders`): the server's candidates, de-duplicated, or `[provider]` when there are none,
   or `[provider]` when the run is pinned or the page forced `?voiceProvider=`.
2. `start()` is single-flight and asks for the microphone **once**; the stream and level meter are reused by every attempt.
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
| Leg fails before live: peer connection failed/closed, provider error event, socket error or close before setup, 15 s deadline, no WebRTC | Yes |
| Any 4xx: consent missing, session not active, content not ready, 400 SDP, 409 `live_voice_time_limit_reached`, `live_voice_session_limit_reached`, `live_voice_transcript_window_closed` | No: terminal, the server's own text is shown |
| 429 `rate_limited` (our per-user limiter, not the provider) | No: one retry on the same provider after about 1.5 s |
| Microphone refused or missing | No: a device problem (shown with the microphone message and, in the apps, an "Open app settings" button) |
| After the connection was live, or once any transcript text exists | No: there is no reconnect and providers are never mixed inside one transcript |
| Pinned run (`?voiceProvider=` or `pinned: true`) | Never |

The create calls are never retried by the API client (`maxRetries: 0`, 22 s timeout): a repeat POST after a
post-creation failure could open a second billed session, and the failover chain is the retry.

**Timing budget.** Preflight is a normal GET. Per provider: OpenAI gathers ICE for up to 5 s, the create call is bounded
at 22 s client side (the server also bounds the provider call and answers `live_voice_provider_timeout` when that is
exceeded), and the link then has 15 s to go live, so the worst case is about 42 s per provider and about 84 s for two.
The typical failover is short: a provider that answers 503 or 429 does so in a fraction of a second, and Gemini is ready
in 2-3 s. On the exam the discussion clock is server-driven (300 s by default), so time spent connecting comes out of the
candidate's card: that is why the create call is not retried and why the deadline is per attempt.

**What the learner sees.** Nothing between attempts. If every candidate fails: one generic message ("The live AI
patient could not start. Please try again.") and one "Start speaking" button, which retries the whole chain and asks
the server for a fresh order first. Server 4xx text is shown as written. Provider names, provider bodies and transport
detail never reach the UI. The provider that served is exposed only as `data-live-provider` and `data-live-failover`
on the mic indicator (for the harness and support screenshots), and the saved transcript already records
`realtime-<provider>`.

**Exam.** Each card has its own hook instance, preflight, provider session and transcript, so failover is per card and
Card B asks the server again (it uses OpenAI again if OpenAI recovered). A forced `?voiceProvider=` pins both cards.
Card A and Card B can therefore end up on different providers; grading reads transcripts only.

**Limits of the design.** A provider session that was created but never connected cannot be closed from the client and
may bill until the provider expires it. Consent copy is generic ("the live voice provider"), so a silent switch changes
the data processor without a new disclosure; that is an owner/legal decision.

## Provider health

Health decides the *order* of the candidates; it is driven by **real session-creation outcomes**, because a `/models`
200 does not prove a session can be created (30 Sep: OpenAI's catalog probe was green while session creation answered
429).

- Every create call is classified. Quota, auth and configuration failures open the provider's circuit at once for a long
  period, rate limits for the provider's own `Retry-After` (bounded), and server errors, network errors, timeouts and
  unparseable answers after repeated failures in a short window. A bad request from the provider (400/422) is counted
  but never opens the circuit, so one browser's or one card's bad request cannot take a provider away from everybody. A
  success closes it.
- After an open period the provider is on probation: the next real failure re-opens it at once, the next success closes it.
- The `/models` probe (every 5 minutes; faster while a provider is unverified or open) is only a **catalog gate**: a
  provider without a passing probe is not offered, but a passing probe never closes a circuit that real failures opened.
- Candidates are `[primary, other]` filtered by configured, verified and not open. No candidate: preflight answers 503
  (`_not_configured` when nothing is configured, otherwise `_unavailable`) and the session DTOs report
  `liveVoiceAvailable: false`, which selects the recorder fallback at page load. A pinned request bypasses the circuit
  but its outcome is still recorded.
- State is in memory in the API slot that serves learners (one upstream at a time). A restart or deploy resets it: the
  cost is at most one fast failed attempt per hard-failing provider, failed over transparently. `/health/ready` **never**
  reads it (readiness gates deploys and rollbacks; a provider outage must not become a deploy outage).
- Admin: `GET /v1/admin/ai/live-voice/health` (`AdminAiConfig`) shows, per provider, whether it is configured and the
  primary, the catalog probe result, the circuit (closed/open/probation, open-until, consecutive failures), the last
  failure (kind, HTTP status, the provider's error code, time), the last success and the counters. The exact provider
  error is captured sanitised: status and error code are logged and shown there; keys, tokens, SDP, card or hidden
  instructions, transcripts and provider bodies never are.

## Hard duration cap

A role-play used to have no server-side limit: the 300 s was card data read by a client countdown, and a client that never
called `/end` left the session active for ever. The server now enforces it. (Numbers are the `LiveVoiceOptions`
defaults at the time of writing; that class is the source of truth.)

- **Deadline** = `RolePlayStartedAt` + the card's `RolePlayTimeSeconds` (300 if missing), capped by
  `MaxRoleplaySeconds` (600). **Hard stop** = deadline + `HardStopGraceSeconds` (30), covering the client's own
  stop-and-save. Both are computed from persisted data with no new column, and returned as `hardStopAt` by the offer, the
  token and `GET /clock` (`rolePlayEndsAt` on the session detail).
- **No new provider session after the deadline** (409 `live_voice_time_limit_reached`) and at most
  `MaxProviderSessionsPerRolePlay` (3) per role-play (409 `live_voice_session_limit_reached`); failed creations do not
  count, so a failover after an OpenAI 429 does not burn the allowance.
- **Gemini**: the ephemeral token expires at `min(now + lifetime, hardStop + 15 s)` (the old 900 s minimum is gone), and
  Gemini closes the socket itself at expiry (1011 "auth token has expired", seen on 25 Sep) even if the browser never does.
- **OpenAI**: the media path is browser to OpenAI, so the server cannot cut a session with its own data. It is bounded
  by refusing new offers after the deadline and the session cap, by the browser (finish at 00:00 and at the `/clock`
  `hardStopAt`), and by the server hanging the provider session up at the hard stop. **Pending verification** that the
  hangup ends an established GPT-Live session; until it is confirmed keep an OpenAI project spend limit as the
  financial backstop.
- **Sweeper**: an idempotent pass in `SpeakingExamAutoAdvanceWorker` (every 20 s) finalises Active AI role-plays past the
  hard stop: compare-and-swap Active to Finished, `EndedAt` = deadline, grading enqueued once, audit event. It runs only in
  the **ai-worker** container (API slots do not run hosted workers), so finalisation can lag by up to about 2 minutes
  around a deploy (the worker stops with a 90 s grace); the wall-clock guards on mint and writes keep correctness in
  the meantime. Exam cards are still closed by the exam clock at `ActiveXStartedAt` + card seconds; the sweeper is only
  their backstop.
- **Late writes**: turns, transcript and recording uploads are accepted while the session is Active and for
  `TranscriptFlushGraceSeconds` (900) after it ended, until grading has started; after that the answer is 409
  `live_voice_transcript_window_closed`. Guards are per child session, so Card A's late save still lands on Card A while
  the exam is on Card B, and a provider session of one card cannot write into the other.
- **Client fail-open** (the exam will not show Card B until Card A's transcript is saved, so a permanent rejection must
  never strand the learner):
  - `stop()` resolves `true` when the transcript was saved, when nothing was said, and when the server will never accept it
    (any 4xx other than 408/429, e.g. `live_voice_transcript_window_closed`). It resolves `false` only for a failure worth
    retrying (network, 5xx, 429), and the next `stop()` retries the same transcript. A turn row that fails to save never
    blocks the transcript.
  - The exam and practice pages stop retrying after **3** failed live-transcript saves and move on. A recording (recorder
    fallback) is never dropped: its upload is retried until it lands.
  - The exam page polls one at a time (an overlapping poll would run the previous card's save again).
  - The practice page starts its countdown from the card's own seconds (no hardcoded 5:00), follows `/clock`, finishes at
    the wall-clock `hardStopAt` if a throttled tab ticks late, and treats 409 `speaking_session_invalid_state` from `/end`
    as "the server already ended it": it carries on to submit and grading.
- A practice role-play that hits the hard stop without a `/submit` is now graded (the 2-credit hold is committed, a free
  sample is consumed), as exam cards already were. **Pending owner confirmation.**

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
`scripts/qa/speaking-live-voice-browser-e2e.mjs` with in-page probes in `scripts/qa/live-voice-browser-probes.mjs`.
Real Chromium, fake microphone playing a scripted candidate once (6 s lead-in, no loop), the QA learner, the real provider.
Runs queue one at a time (concurrency group), never dispatch during a deploy.

- Inputs: `mode` practice | exam; `script`; `voice` piper | espeak; `voice_provider` (blank = the server's health order;
  a provider set here is **pinned**, so it never fails over and every check stays strict); `expected_primary`
  (blank = no assertion; fails the run if the first provider call goes elsewhere); `fail_primary` (answers the expected
  primary's create call with a 503 in the browser and asserts the failover; needs `expected_primary` and a blank
  `voice_provider`); `speak_seconds` (practice; blank = the tape's last speech + 10 s, max 280 because the app auto-ends
  at 5:00). Scripts: `compare` (about 4 minutes of tape with room for the patient to finish, an early teach-back probe
  before any explanation and a late one after it) and `full-lactose` are written for the lactose practice card and are
  rejected in exam mode; `smoke` is the behaviour probe (barge-in, 25/45/60 s silences, teach-back before explaining);
  `short` and `generic` are minimal.
- Artifacts (`live-voice-e2e`): `metrics.json` (usage, latency, barge-in / talk-over / silences, stability incl.
  WebSocket close codes and RTC states, `providerCalls`, `providerAttempts` (call, HTTP status, the app's error code,
  `hardStopAt`; never a body or token), `failoverObserved`, `hardStopAt`, per-card `Panel` (the provider the page reports
  serving) and `MicStreams` (track states, to see the microphone released between cards), checks),
  `timeline-events.json` (timed words incl. provider `start_ms`/`end_ms`), `saved-transcript-<session>.json` (the
  transcript API body the grader reads), `card-text.json`, `patient-audio-<n>.webm` (what was audible, both providers),
  screenshots.
- Checks (a red check fails the run): `savedTranscriptsCaptured`, `noSplitSentences` (only when GPT-Live served),
  `staysInRole`, `noBrowserErrors`, `bargeInPatientStops`, `noPatientTalkOver`, `survivesSilences`,
  `primaryProviderIsExpected` (only with `expected_primary`), `failoverAsRequested` (only with `fail_primary`: per card
  exactly one call to the failed primary then one to the other provider, and the page reports the other one serving) and
  `cardLabelsBySlot` (exam: Card A's text says "Role-Play Card A", Card B's says "Role-Play Card B"). A 503 on one
  provider followed by a success on the other is a *failover*, not a browser error, but only in an unpinned run; usage
  and `noSplitSentences` follow the provider that actually served. Gemini patient speech is measured from playback
  timing, so its barge-in/talk-over numbers are not comparable with GPT-Live's pass/fail thresholds without care.
  `metrics.leakCheck` (exam) is a bag-of-words suspect list, not a check: read both transcripts. Teach-back is judged by
  reading the saved transcript, not by the harness.
- What to run after this change: an unpinned exam with `expected_primary` set to the current first choice; the same with
  `fail_primary` (practice and exam); a pinned run per provider for the like-for-like comparison.

## Known open items

- Nothing in this close-out has run in production yet: failover, the health model, the hard duration cap, the card labels
  and the client fail-open are all unverified. No E2E has run since 26 Sep 19:27 UTC either, so PR #265 (AI Assistant
  401 loop, grading retry) and every later change are unverified too.
- The exact cause of the 30 Sep OpenAI 429 (quota, rate limit or project budget) is unread: the provider's body used to be
  discarded. The sanitised capture and the admin health endpoint now show the provider's error code; read it there.
- The 26 Sep two-card mock ended red; only 1 of 4 exam runs that completed both cards was ever graded (bugs fixed by #259/#264).
- Gemini vs OpenAI: one matched pair (n=1 each); no complete graded Gemini run. Claude grading cost unmeasured.
- Live-voice sessions store no audio; the results copy still says "We received your recording".
- No reconnect; `?voiceProvider=` is not restricted (any learner can force the costlier provider or bypass the circuit);
  hidden information is prompt-only.
- A provider session that was created but never connected (failover after a successful create, or a dead tab) may bill
  until the provider expires it; the app stores only a hash of the provider session id and cannot close it later.
- Silent failover changes the data processor under generic consent copy ("the live voice provider"): owner/legal decision.
- When every provider fails the learner can only retry; a recorder fallback for that case would change what is graded and
  needs the owner's approval.
- OpenAI over a UDP-blocked network (hospital Wi-Fi) has no TURN/STUN configured and now fails after the 15 s deadline; it
  matters when OpenAI is the fallback.
- Card A and Card B can be served by different providers after a circuit change (per-exam provider analysis is mixed).
- The deploy workflow asserts no build identity; only the local `scripts/ship/watch-deploy.ps1` does (`LIVE_SHA_OK`).
- Scheduled Speaking Playwright and accessibility suites: last green 7-8 Sep; the failures on 28 Sep were real
  (auth refresh 400, target-size); the 29 Sep runs were Actions billing blocks.
