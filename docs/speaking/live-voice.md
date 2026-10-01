# Speaking live voice agent (AI patient)

Record of the September 2026 rollout and testing. Written 2026-09-30 from the code, GitHub Actions logs and the
run artifacts of the 25-26 Sep production E2E runs; extended the same day with the close-out of the 30 Sep incident
(OpenAI session creation answered 429, so learners saw "could not start this conversation" and there was no
fallback). **Items marked "pending" have not been verified in production.** Everything under "Provider failover",
"Provider health", "Hard duration cap", "Consent and disclosure" and "Card labels" is new in that close-out and is
pending until the production E2E below has been run against it.

Reconciled on 2026-09-30 with the final code of the close-out branch (`feat/speaking-resilience-2026-09-30`), which is the
source of truth where this file and the code differ: `LiveVoiceOptions`, `LiveVoiceService`,
`LiveVoiceProviderProbeState`, `SpeakingExamAutoAdvanceWorker` and `SpeakingSessionService`. Nothing in the close-out has
run in production yet.

## Providers

| | OpenAI GPT-Live (production default) | Gemini Live |
| --- | --- | --- |
| Model | `gpt-live-1`, WebRTC via `POST /v1/live/sessions` | `models/gemini-3.8-live`, browser WebSocket with an ephemeral token |
| First choice | `LIVEVOICE__PRIMARYPROVIDER=openai` (code default, `appsettings.json`, compose): the first candidate whenever it is configured, verified and its circuit is not open; Gemini is then the failover | `LIVEVOICE__PRIMARYPROVIDER=gemini`; or `?voiceProvider=gemini` on the practice/exam page, which **pins** the run to that provider (no failover, circuit bypassed). Any learner can pin if the provider is configured and verified; there is no role or flag gate |
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
  `providerCalls[0] == "gemini/token"` and the `primaryProviderIsExpected` check green. `GET /v1/admin/ai/live-voice/health`
  on the serving slot should list `gemini` first in `candidateOrder` and mark it `primary`.

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
  **Pending: not yet verified in production** (run the E2E and read `noSplitSentences`).
- Prompt rules: candidate-first; TEACH-BACK (never fill the gap from the card; RULE_18 is the separate honest-response
  rule); and, added with this change and **pending verification**, never attribute unsaid treatments, tests or
  referrals to the doctor (2 of 5 post-#257 teach-back replies did).
- No audio is stored ("Recording unavailable" on the transcript page); grading sees the transcript only.
- A connection that drops mid-conversation, or a patient that stays silent, is restored automatically up to twice per card
  ([Mid-session recovery](#mid-session-recovery)); after that (or for a pinned provider) the error is shown as before. A
  provider that cannot *start* is replaced by the other one automatically ([Provider failover](#provider-failover)). The recorder fallback is used
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
  about 8-9 minutes to grade on the API route in the 26 Sep runs (PR #259 quotes 11-12); the sidecar's latency has not
  been measured. Deploys stop the AI worker with a 90 s grace, so an in-flight grade is requeued and restarts from zero
  (derived from the code; not observed in production).

## Provider failover

Failover is executed by the browser and decided by the server, because the OpenAI leg needs a browser-made SDP offer
and the Gemini leg needs the browser to open the WebSocket: the server cannot finish a cross-protocol failover alone.

**Contract** (`lib/api/speaking-live-voice.ts`; all additions are optional, so an old client or an old server keeps working):

- `GET /v1/speaking/realtime/sessions/{id}/preflight[?provider=]` returns its usual fields plus `candidates` (the
  providers to try, in order: the configured primary first, then the other, each only if it is configured,
  catalog-verified and its circuit is not open; `provider` is always `candidates[0]`) and `pinned` (true when the caller
  forced a provider: one candidate, circuit bypassed). No `candidates` (old server) means one attempt with `provider`.
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
   or `[provider]` when the run is pinned or the page forced `?voiceProvider=`.
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
| Microphone refused or missing | No: a device problem (shown with the microphone message, which on the live and recorder panels ends "press Start speaking again", and, in the apps, an "Open app settings" button) |
| After the connection was live, or once any transcript text exists | Not at start: a dead link is restored by [Mid-session recovery](#mid-session-recovery) (same provider first, the other one on the second restore); the transcript stays one continuous list |
| Pinned run (`?voiceProvider=` or `pinned: true`) | Never |

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
Card B asks the server again (it uses OpenAI again if OpenAI recovered). A forced `?voiceProvider=` pins both cards.
Card A and Card B can therefore end up on different providers; grading reads transcripts only.

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
  restores). A provider forced with `?voiceProvider=` or pinned by the server **never recovers**, so comparison and QA runs
  measure the raw stability of the provider they asked for. No restore after the learner pressed stop or left.
- **Known limits.** Audio spoken while the link is down is lost. The candidate's last unanswered sentence is in the replayed
  history, so the patient waits for the candidate to speak next; a candidate who is waiting for the answer must speak again.
  The stall detector reads the microphone level (no echo handling beyond the browser's echo cancellation).

### Gemini candidate timing

Gemini gives the candidate's transcript no timing; every candidate segment used to be zero-length (start = end = the moment
the text arrived), which the grader flagged ("the candidate segment has zero duration ... may be a capture error") and which
hid the real order of speech. The hook now finds the candidate's speech bursts from the microphone level
(`createSpeechTracker`: level >= 0.05 on the meter's scale, a burst ends after 700 ms of quiet, bursts under 400 ms are
dropped) and gives a transcript fragment the span of its burst: the closed bursts since the last fragment, or the burst so
far when the text arrives mid-sentence. Patient segments keep their arrival time. OpenAI keeps its own `start_ms`/`end_ms`.

## Consent and disclosure

- The learner-facing consent step (`SpeakingRulesConsent`) shows generic copy ("... your microphone is streamed in real
  time to the live voice provider"). It names no provider, and the client does not render the preflight `disclosure`.
- The server's preflight `disclosure` names **every** candidate, in try order, each with its model. With two candidates it
  names the first, says the service may switch to the next one if the first cannot start the conversation, names that
  one too and says the microphone audio goes to "whichever of these providers serves your session". With one candidate
  (pinned through `?voiceProvider=`, or only one healthy) it names that provider only, as before. It no longer names only
  the primary.
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
  pinned request bypasses the circuit (it still needs the provider configured and verified) and its outcome is still
  recorded.
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
`scripts/qa/speaking-live-voice-browser-e2e.mjs` with in-page probes in `scripts/qa/live-voice-browser-probes.mjs` and the
served-provider attribution in `scripts/qa/live-voice-served-provider.mjs` (the workflow copies all three next to `e2e.mjs`).
Real Chromium, fake microphone playing a scripted candidate once (6 s lead-in, no loop), the QA learner, the real provider.
Runs queue one at a time (concurrency group), never dispatch during a deploy.

- Inputs: `mode` practice | exam; `script`; `voice` piper | espeak; `voice_provider` (blank = the server's candidates,
  primary first; a provider set here is **pinned**, so it never fails over and every check stays strict); `expected_primary`
  (blank = no assertion; fails an unpinned run if the first provider call goes elsewhere; **ignored when `voice_provider`
  pins a provider**: `metrics.expectedPrimaryIgnored` is true, a log line says so and `primaryProviderIsExpected` stays
  null); `fail_primary` (answers the expected primary's create call with a 503 in the browser and asserts the failover;
  needs `expected_primary` and a blank `voice_provider`. The faked answer is produced in the browser and **never reaches
  the API**, so only the client failover is exercised: the server's circuit, health model, provider-error capture and
  audit never see it); `speak_seconds` (practice; blank = the tape's last speech + 10 s, max 280 because the app auto-ends
  at 5:00). Scripts: `compare` (about 4 minutes of tape with room for the patient to finish, an early teach-back probe
  before any explanation and a late one after it) and `full-lactose` are written for the lactose practice card and are
  rejected in exam mode; `smoke` is the behaviour probe (barge-in, 25/45/60 s silences, teach-back before explaining);
  `short` and `generic` are minimal.
- Artifacts (`live-voice-e2e`): `metrics.json` (usage, latency, barge-in / talk-over / silences, stability incl.
  WebSocket close codes and RTC states, `providerCalls` (every create call, failed ones included), `providerAttempts`
  (call, HTTP status, the app's error code, `hardStopAt`; never a body or token), `failoverObserved`, `hardStopAt`,
  `servedProvider` (`openai` | `gemini` | `mixed` for an exam whose cards were served by different providers | null; read
  from each connected card's panel `data-live-provider`, falling back to the 2xx create calls on a build without it),
  `expectedPrimaryIgnored`, per-card `Panel` (the provider the page reports serving) and `MicStreams` (track states, to
  see the microphone released between cards), checks),
  `timeline-events.json` (timed words incl. provider `start_ms`/`end_ms`), `saved-transcript-<session>.json` (the
  transcript API body the grader reads), `card-text.json`, `patient-audio-<n>.webm` (what was audible, both providers),
  screenshots.
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
  (only with `fail_primary`: per card exactly one call to the failed primary then one to the other provider, and the page
  reports the other one serving), `recoveredAsRequested` (only with a fault input, see [Fault injection](#fault-injection-mid-session-recovery))
  and `cardLabelsBySlot` (exam: Card A's text says "Role-Play Card A", Card B's says
  "Role-Play Card B"). A 503 on one provider followed by a success on the other is a *failover*, not a browser error, but
  only in an unpinned run; usage and `noSplitSentences` follow the provider that actually served. Gemini patient speech
  is measured from playback timing, so its barge-in/talk-over numbers are not comparable with GPT-Live's pass/fail
  thresholds without care.
  `metrics.leakCheck` (exam) is a bag-of-words suspect list, not a check: read both transcripts. Teach-back is judged by
  reading the saved transcript, not by the harness.
- What to run after this change: an unpinned exam with `expected_primary` set to the current first choice; the same with
  `fail_primary` (practice and exam); a pinned run per provider for the like-for-like comparison. A `fail_primary` run
  proves the client failover only. The circuit, `hardStopAt` and the OpenAI hang-up need a real, unfaked run plus a read
  of `providerAttempts`, `GET /v1/admin/ai/live-voice/health` and the ai-worker's hang-up log lines.

### Fault injection (mid-session recovery)

Two optional inputs (blank = off, the run is unchanged) break the live link on purpose so the app's mid-session recovery can be
measured. They hit only the first live conversation (practice, or exam Card A), N seconds after the candidate microphone tape
starts. One fault per run: `fault_drop_at_s` wins when both are set (`metrics.fault.stallIgnored`).

- `fault_drop_at_s` (env `FAULT_DROP_AT_S`): kills the live provider connection from inside the page. Gemini: its WebSocket is
  closed (marked `injected` in `stability.wsClose`). OpenAI: the `oai-events` data channel is closed, because its `close` event
  is what the app listens to (`peer.close()` alone fires nothing locally).
- `fault_stall_at_s` (env `FAULT_STALL_AT_S`): the provider goes silent. Every server-to-client message (Gemini frames, OpenAI
  data-channel events) is swallowed before the app and the recording probes see it, and the patient's WebRTC audio is silenced.
  It ends by itself when the app builds a NEW transport, so the recovered session behaves normally. It exercises the app's
  silence watchdog, so the candidate must still speak after the stall began. Gemini's wire-level records (`transcript.json`,
  the timeline words, usage, `patient-audio-raw.wav`) still include what the stall hid from the app; what the learner could
  hear is the speech spans and `patient-audio-<n>.webm`.
- Rejected with `voice_provider` (a pinned provider never recovers, so the fault would only kill the session) and with
  `fail_primary` (its check expects one create call per provider, a recovery makes another): by a workflow guard step and again
  by the harness, before any provider is billed. The fault time must be before the conversation ends (`speak_seconds`, or 280 in
  exam mode). Use `short` or `generic`: `smoke`'s 25-60 s silences would turn `survivesSilences` red for the wrong reason.
- Metrics: `metrics.fault` = `{ kind: 'drop' | 'stall' | null, atSeconds, firedAt (epoch ms, null = it never fired), provider
  (the transport hit), recoveredAt (the first provider create call after the fault, once the panel reports a recovery),
  swallowed (stall: messages hidden from the app), stallIgnored, error (only when no live transport was found) }`.
  `metrics.recoveries` = the panel's `data-live-recoveries` (exam: the total, with `recoveriesA` / `recoveriesB`; 0 when the
  attribute is absent, null when the panel was never read). It is recorded on every run, fault or not.
- Check `recoveredAsRequested` (null without a fault): the fault fired, the faulted card's panel reports at least 1 recovery,
  the patient produced a speech span or a transcript delta after the recovery session was asked for, and the panel did not show
  its error alert at its last live reading. The "Reconnecting the patient…" text is not required.

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

- Nothing in this close-out has run in production yet: failover, the health model, the hard duration cap, the OpenAI
  hang-up, the card labels and the client fail-open are all unverified. No E2E has run since 26 Sep 19:27 UTC either, so
  PR #265 (AI Assistant 401 loop, grading retry) and every later change are unverified too.
- The exact cause of the 30 Sep OpenAI 429 (quota, rate limit or project budget) is unread: the provider's body used to be
  discarded. The provider's error code and, for a 429 or a 5xx from the vendor host, its redacted message are now in the
  failure log line, and the class, status and code are on the admin health endpoint; read it there.
- The 26 Sep two-card mock ended red; only 1 of 4 exam runs that completed both cards was ever graded (bugs fixed by #259/#264).
- Gemini vs OpenAI: one matched pair (n=1 each); no complete graded Gemini run. Claude grading cost unmeasured.
- Live-voice sessions store no audio; the results copy still says "We received your recording".
- Recovery is best-effort (two restores per card, none for a pinned provider). Measured in production on 1 Oct 2026 (OpenAI, build 74ca2607b, compare script, unpinned): a dropped data channel at 60 s had a new session offered 0.6 s later and connected 1.7 s after the drop (2 OpenAI offers, 16 saved segments, no split sentence, 308/500); a stalled provider was restored 94 s after the stall (the sentence-clock defect above, fixed since). Gemini recovery is covered by unit tests only, because the fault runs cannot pin a provider; `?voiceProvider=` is not restricted (any learner can force the costlier provider or bypass the circuit);
  hidden information is prompt-only.
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
