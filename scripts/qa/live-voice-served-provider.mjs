// Which provider(s) served a live-voice E2E run, and the conversation record attributed to them
// (used by speaking-live-voice-browser-e2e.mjs; the workflow copies this file next to it, so keep it self-contained).
// It also holds the pure helpers of the run's other verdicts, after createServedRecord: the fault-injection runs
// (parseFault, recoveredAsRequested, failoverCallsOk), the QA provider pin (pinHonoured), the credit ledger (creditPreflight,
// creditVerdict, gradeRetryVerdict), the History rows (historyVerdict), the results wording (resultsWordingVerdict) and the
// console check (isNavigationAbortNoise). Transcript judgement lives in live-voice-transcript-quality.mjs.
//
// Data-channel events, socket frames and create calls also come from a leg that failed over (an error event, a close,
// a 503), so none of them says who served. What each connected card's panel reports (data-live-provider) does. A build
// that predates the attribute reports nothing; the create calls that succeeded then stand in for it.
//
//   panels()      the panel record of each card ({ provider, failedOver } or undefined when the card never connected)
//   servedCalls() create calls that answered 2xx, in order ('openai/offer' | 'gemini/token')
//   gemini        { words, errors } collected from the Gemini socket frames, whoever served
//   transcript, stability  the run's records that applyServed rewrites
export function createServedRecord({ panels, servedCalls, gemini, transcript, stability }) {
  const reportedProviders = () => panels().map((p) => p?.provider).filter(Boolean);
  const servedProviders = () => {
    const reported = reportedProviders();
    return reported.length ? reported : servedCalls().map((call) => call.split('/')[0]);
  };
  const openAiServed = () => servedProviders().includes('openai');
  const geminiServed = () => servedProviders().includes('gemini');
  // OpenAI's timed transcript words from its data-channel events (start/end are the provider's own timeline).
  const openAiWords = (events) => events.filter((e) => /transcript\.delta$/.test(e.type)).map((e) => ({
    at: e.__at, who: e.type.includes('input') ? 'candidate' : 'patient', text: e.delta, startMs: e.start_ms ?? null, endMs: e.end_ms ?? null,
  }));
  // Rebuilds what the run heard from the provider(s) that served: OpenAI's from its data-channel events, Gemini's from its
  // socket frames. A leg that failed over leaves events or frames behind and they are not the conversation; an exam whose
  // cards were served by different providers keeps both.
  function applyServed(events) {
    const openAi = openAiServed() ? events : [];
    const said = (type, who) => [
      ...openAi.filter((e) => e.type === type).map((e) => ({ at: e.__at, text: e.delta ?? '' })),
      ...(geminiServed() ? gemini.words.filter((w) => w.who === who) : []),
    ].sort((a, b) => a.at - b.at).map((w) => w.text).join('');
    transcript.candidate = said('session.input_transcript.delta', 'candidate');
    transcript.patient = said('session.output_transcript.delta', 'patient');
    stability.providerErrors = [
      ...(geminiServed() ? gemini.errors : []),
      ...openAi.filter((e) => String(e.type).includes('error')).map((e) => JSON.stringify(e)),
    ];
    stability.sessionClosed = openAi.filter((e) => e.type === 'session.closed').map((e) => ({ reason: e.reason, usage: e.usage }));
  }
  return { reportedProviders, servedProviders, openAiServed, geminiServed, openAiWords, applyServed };
}

// Fault injection for the mid-session runs (FAULT_DROP_AT_S / FAULT_STALL_AT_S / FAULT_RELOAD_AT_S, see the E2E script).
//
// Which fault a run asked for. Blank = off; one fault per run, DROP wins over STALL wins over RELOAD (the lost ones are
// reported as stallIgnored / reloadIgnored). Throws on a value that can never fire, and on a drop or a stall with a pinned
// provider: the app never recovers a pinned provider (VOICE_PROVIDER), so the fault would only kill the session. A reload
// needs no recovery, so it is allowed with a pinned provider. FAIL_PRIMARY is no longer a conflict: failoverCallsOk expects
// the extra create calls a recovery or a reload makes.
//   maxAtSeconds  the fault must fire before this many seconds of the conversation have passed
export function parseFault({ dropAt = '', stallAt = '', reloadAt = '', pinnedProvider = '', maxAtSeconds = Infinity }) {
  const seconds = (name, raw) => {
    const text = String(raw ?? '').trim();
    if (!text) return null;
    const value = Number(text);
    if (!Number.isFinite(value) || value <= 0) throw new Error(`${name} must be a number of seconds above 0, not "${text}".`);
    if (value >= maxAtSeconds) throw new Error(`${name}=${text} is not before the end of the conversation (${maxAtSeconds} s: SPEAK_SECONDS in practice, the card's 5:00 limit in an exam), so the fault would never fire.`);
    return value;
  };
  const drop = seconds('FAULT_DROP_AT_S', dropAt);
  const stall = seconds('FAULT_STALL_AT_S', stallAt);
  const reload = seconds('FAULT_RELOAD_AT_S', reloadAt);
  if (drop === null && stall === null && reload === null) return { kind: null, atSeconds: null, stallIgnored: false, reloadIgnored: false };
  if (pinnedProvider && (drop !== null || stall !== null)) throw new Error('FAULT_DROP_AT_S / FAULT_STALL_AT_S need a blank VOICE_PROVIDER: a pinned provider never recovers, so the fault would only kill the session.');
  if (drop !== null) return { kind: 'drop', atSeconds: drop, stallIgnored: stall !== null, reloadIgnored: reload !== null };
  if (stall !== null) return { kind: 'stall', atSeconds: stall, stallIgnored: false, reloadIgnored: reload !== null };
  return { kind: 'reload', atSeconds: reload, stallIgnored: false, reloadIgnored: false };
}

// The provider create calls (request order) a FAIL_PRIMARY run makes. Per card: the failed primary, then the secondary that
// serves. A fault hits the first card only. Each recovery adds calls: the first restore retries the provider that served (the
// secondary), the second tries the other one first (the primary, failed again) and then the secondary. A reload remounts the
// panel, which walks the candidates again: primary, secondary. Anything else, in any order, is a failover bug.
//   input  { calls, primaryCall, secondaryCall, cards, recoveries?, reloads? }  calls = providerCalls of the run
/** @param {any} input */
export function failoverCallsOk(input) {
  const { calls, primaryCall, secondaryCall, cards, recoveries = 0, reloads = 0 } = input;
  const want = [];
  for (let card = 0; card < cards; card += 1) {
    want.push(primaryCall, secondaryCall);
    if (card !== 0) continue;
    for (let r = 1; r <= recoveries; r += 1) want.push(...(r === 1 ? [secondaryCall] : [primaryCall, secondaryCall]));
    for (let r = 0; r < reloads; r += 1) want.push(primaryCall, secondaryCall);
  }
  return JSON.stringify(calls) === JSON.stringify(want);
}

// metrics.checks.recoveredAsRequested: null when no fault was requested or the fault is a page reload (it has no recovery,
// see metrics.reload); otherwise true only when the fault really fired,
// the panel reports at least one recovery, the conversation did not end in the error state, and the patient spoke again
// (an audible span or a transcript delta) after the recovery session was asked for.
//   fault       { kind, firedAt, recoveredAt }: firedAt null = the fault never fired; recoveredAt = the first provider
//               create call after it (null = none seen, then the fault time stands in)
//   recoveries  the faulted card's data-live-recoveries (null = the panel was never read)
//   errorShown  whether the panel showed its error alert at the last reading while the conversation was live (null = unknown)
//   patientAt   epoch ms of every patient audio span start and patient transcript delta of the run
export function recoveredAsRequested({ fault, recoveries, errorShown, patientAt }) {
  if (!fault?.kind || fault.kind === 'reload') return null;
  if (!fault.firedAt) return false;
  const since = fault.recoveredAt ?? fault.firedAt;
  return (recoveries ?? 0) >= 1 && errorShown !== true && patientAt.some((t) => t > since);
}

// QA provider pin. Since the pin gate the server honours GET .../preflight?provider=<p> only for a learner with an enabled
// feature flag speaking_live_voice_pin:<learner user id>; for anyone else it answers 200 with the normal automatic order and
// pinned:false. A VOICE_PROVIDER run that merely landed on the primary provider would then still pass pinnedProviderServed,
// although nothing was pinned, so the answer itself is judged: every answered (HTTP 200) preflight that carried provider=
// says pinned:true for the provider asked for. Null when no provider was requested.
//   preflights  [{ requested, status, pinned, provider }] one per GET .../preflight?provider=... of the run
export function pinHonoured(preflights, provider) {
  if (!provider) return null;
  const answered = (preflights ?? []).filter((p) => p.status === 200);
  return answered.length > 0 && answered.every((p) => p.pinned === true && p.provider === provider);
}

// The fail-fast text for a run whose pin the server ignored (the learner id is read from the sign-in token when possible).
export function pinIgnoredMessage(provider, learnerId) {
  return `The server ignored voice_provider=${provider}: enable the feature flag speaking_live_voice_pin:${learnerId || '<learner user id>'} for the QA learner in Admin > Feature Flags (docs/speaking/live-voice.md, QA provider pin).`;
}

// The learner user id in a bearer token: its "sub" claim is the NameIdentifier the pin flag key is built from. Null when the
// token is missing or not a JWT.
export function learnerIdFromBearer(bearer) {
  try {
    const token = String(bearer ?? '').replace(/^Bearer\s+/i, '');
    const payload = JSON.parse(Buffer.from(token.split('.')[1] ?? '', 'base64url').toString('utf8'));
    return typeof payload.sub === 'string' && payload.sub ? payload.sub : null;
  } catch {
    return null;
  }
}

// AI credits (GET /v1/me/ai-package-credits). A Speaking card costs 2 credits from any pool; the ledger row of a debit has
// reason GradingDeduct and the hold's business reference (exam:{examId}:cardA / :cardB, practice:{sessionId}); a refund is
// RefundOnFailure on "<reference>:release"; a mock exam unit pays with MockDeduct instead and moves no credits.
const POOLS = ['sharedCredits', 'flexibleCredits', 'speakingOnlyCredits', 'writingOnlyCredits'];
const DELTAS = ['sharedCreditsDelta', 'flexibleCreditsDelta', 'speakingOnlyCreditsDelta', 'writingOnlyCreditsDelta'];
const sumOf = (record, keys) => keys.reduce((total, key) => total + (Number(record?.[key]) || 0), 0);
export const creditPools = (snapshot) => sumOf(snapshot, POOLS);
export const creditRowDelta = (row) => sumOf(row, DELTAS);

// Why a run must not start (nothing is spent yet): a credit assertion proves nothing when the account is funded some other
// way, or cannot fund the run. Empty = go. options { activities, exam, now? }: activities = cards the run holds (exam 2,
// practice 1); exam = a mock exam unit would pay for the exam instead of credits.
export function creditPreflight(snapshot, options) {
  const { activities, exam = false, now = Date.now() } = options;
  if (!snapshot) return ['the credit balance could not be read'];
  const reasons = [];
  if (snapshot.speakingUnlimited) reasons.push('the learner has unlimited Speaking, so nothing would be charged');
  if (exam && Number(snapshot.mockExamsRemaining) > 0) reasons.push('a mock exam unit would pay for the exam instead of credits');
  // availableSpeakingActivities is the server's own count of cards the pools can fund; an older server omits it.
  const fundable = Number.isFinite(Number(snapshot.availableSpeakingActivities))
    ? Number(snapshot.availableSpeakingActivities)
    : Math.floor(sumOf(snapshot, ['sharedCredits', 'flexibleCredits', 'speakingOnlyCredits']) / 2);
  if (fundable < activities) reasons.push(`only ${fundable} Speaking activities can be funded and ${activities} are needed`);
  const expires = Date.parse(snapshot.expiresAt ?? '');
  if (Number.isFinite(expires) && expires < now + 2 * 86_400_000) reasons.push('the credits expire within two days');
  return reasons;
}

/**
 * metrics.checks.creditsDeductedOnce: the run was charged exactly once, in the right places, and never refunded.
 * @param {any} input { before, after, prefix, expectedRefs, steps? }
 *   before        the balance read before anything was spent
 *   after         the balance read at the end (its transactions carry the ledger)
 *   prefix        every ledger reference of this run starts with it ("exam:<examId>:" / "practice:<sessionId>")
 *   expectedRefs  the exact references that must each hold one 2-credit GradingDeduct row (exam: cardA, cardB; practice: one)
 *   steps         [{ name, snapshot, delta }]: the balance after the hold of each card and after grading, each moved by delta
 * Returns { ok, problems, delta, rows }.
 */
export function creditVerdict(input) {
  const { before, after, prefix, expectedRefs, steps = [] } = input;
  const expected = expectedRefs.length * 2;
  const problems = [];
  if (!before || !after) return { ok: false, problems: ['the balance before or after the run could not be read'], delta: null, rows: [] };
  const rows = (after.transactions ?? []).filter((t) => String(t.referenceId ?? '').startsWith(prefix));
  const reasonOf = (row) => String(row.reason ?? '');
  const debits = rows.filter((t) => reasonOf(t) === 'GradingDeduct');
  const refs = debits.map((t) => t.referenceId).sort();
  if (JSON.stringify(refs) !== JSON.stringify([...expectedRefs].sort())) problems.push(`debit rows ${JSON.stringify(refs)}, expected ${JSON.stringify(expectedRefs)}`);
  if (debits.some((t) => creditRowDelta(t) !== -2)) problems.push('a debit row is not exactly 2 credits');
  if (rows.some((t) => /mock/i.test(reasonOf(t)))) problems.push('a mock exam unit paid, not credits');
  if (rows.some((t) => /refund/i.test(reasonOf(t)))) problems.push('a refund row exists');
  const delta = creditPools(after) - creditPools(before);
  if (delta !== -expected) problems.push(`the balance moved by ${delta}, expected -${expected}`);
  for (const step of steps) {
    if (!step.snapshot) { problems.push(`${step.name}: the balance could not be read`); continue; }
    const moved = creditPools(step.snapshot) - creditPools(before);
    if (moved !== step.delta) problems.push(`${step.name}: the balance moved by ${moved}, expected ${step.delta}`);
  }
  return {
    ok: problems.length === 0, problems, delta,
    rows: rows.map((t) => ({ referenceId: t.referenceId, reason: reasonOf(t), delta: creditRowDelta(t) })),
  };
}

/**
 * metrics.checks.gradeRetryIdempotent: asking for the grade again after it was graded is a safe no-op.
 * @param {any} input { retries, ledgerBefore?, ledgerAfter? }
 *   retries       [{ sessionId, status, assessmentIdBefore, assessmentIdAfter }] one per card: POST /ai-assess answered 200 or
 *                 202, and the assessment is still the same one
 *   ledgerBefore  the run's ledger rows before the retry, ledgerAfter after it (any comparable value; null = not read)
 * Returns { ok, problems }.
 */
export function gradeRetryVerdict(input) {
  const { retries, ledgerBefore = null, ledgerAfter = null } = input;
  const problems = [];
  if (!retries?.length) problems.push('no retry was made');
  for (const r of retries ?? []) {
    if (r.status !== 200 && r.status !== 202) problems.push(`${r.sessionId}: the retry answered ${r.status}`);
    if (!r.assessmentIdBefore) problems.push(`${r.sessionId}: no assessment existed before the retry`);
    else if (r.assessmentIdAfter !== r.assessmentIdBefore) problems.push(`${r.sessionId}: the assessment changed (${r.assessmentIdBefore} to ${r.assessmentIdAfter})`);
  }
  if (ledgerBefore !== null && ledgerAfter !== null && JSON.stringify(ledgerBefore) !== JSON.stringify(ledgerAfter)) problems.push('the credit ledger changed');
  return { ok: problems.length === 0, problems };
}

/**
 * metrics.checks.historyListsExam: the learner's History (GET /v1/me/attempts, the "Attempt activity" list) shows the run as
 * ONE row with its route, credits and score, and Past evidence (GET /v1/submissions) does not list it.
 * @param {any} input { kind, examId?, sessionId?, attempts, submissions, runStartMs, expectedCredits, expectedScore?, pageText? }
 *   kind             'exam' (one row titled "Full Speaking Mock", attemptId = exam id) or 'practice' (the row whose route is the
 *                    session's results page)
 *   attempts         the items of /v1/me/attempts
 *   submissions      the items of /v1/submissions (null = not captured, then not judged)
 *   runStartMs       when the run began; any Speaking row started since is this run's
 *   expectedCredits  4 for an exam, 2 for practice
 *   expectedScore    the score the results page showed (the combined score / the card's), null = unknown
 *   pageText         what the History page showed (null = not captured): it must show the row's label
 * Returns { ok, problems, row }.
 */
export function historyVerdict(input) {
  const { kind, examId, sessionId, attempts, submissions, runStartMs, expectedCredits, expectedScore = null, pageText = null } = input;
  const problems = [];
  const speaking = (item) => String(item?.subtest ?? '').toLowerCase() === 'speaking';
  const sinceRun = (value) => Date.parse(value ?? '') >= runStartMs - 60_000;
  const route = kind === 'exam' ? `/speaking/exam/${examId}/results` : `/speaking/sessions/${sessionId}/results`;
  const rows = (attempts ?? []).filter((a) => (kind === 'exam' ? a.attemptId === examId : a.route === route));
  const row = rows[0] ?? null;
  if (rows.length !== 1) problems.push(`the run is listed ${rows.length} times, expected once`);
  const started = (attempts ?? []).filter((a) => speaking(a) && sinceRun(a.startedAt));
  if (started.length !== 1) problems.push(`${started.length} Speaking rows were started in this run, expected 1 (a mock must be one row, not one per card)`);
  if (row) {
    if (kind === 'exam' && row.title !== 'Full Speaking Mock') problems.push(`the row is titled "${row.title}", expected "Full Speaking Mock"`);
    if (kind === 'exam' && row.contentRef !== examId) problems.push(`the row's contentRef is ${row.contentRef}, expected the exam id`);
    if (row.route !== route) problems.push(`the row's route is ${row.route}, expected ${route}`);
    if (row.status !== 'completed') problems.push(`the row's status is ${row.status}, expected completed`);
    if (row.creditsUsed !== expectedCredits) problems.push(`the row shows ${row.creditsUsed} credits used, expected ${expectedCredits}`);
    if (!/^\d{1,3}\/500$/.test(String(row.resultLabel ?? ''))) problems.push(`the row's result label is ${JSON.stringify(row.resultLabel ?? null)}, expected a score such as 192/500`);
    else if (expectedScore !== null && row.resultLabel !== `${expectedScore}/500`) problems.push(`the row says ${row.resultLabel} but the results page showed ${expectedScore}/500`);
    if (pageText !== null && row.resultLabel && !pageText.replace(/\s+/g, '').includes(String(row.resultLabel).replace(/\s+/g, ''))) problems.push('the History page does not show the row\'s result label');
  }
  const pastEvidence = submissions === null || submissions === undefined ? 0 : submissions.filter((s) => speaking(s) && sinceRun(s.attemptDate)).length;
  if (pastEvidence) problems.push(`Past evidence lists ${pastEvidence} Speaking attempt(s) of this run, which have no score to show`);
  return { ok: problems.length === 0, problems, row };
}

// Learner-facing results wording (metrics.checks.resultsWordingHonest). A live conversation stores no audio, so no results
// page may say "recording" (outside the transcript tab) or show an audio player, each must say it was a live conversation, and
// the exam results show a readable band and the advisory sentence.
const RECORDING = /recording/i;
const LIVE_COPY = /transcript of your live conversation|no audio recording is stored for live conversations/i;
const SUBMISSION_BANNER = /transcript of your live conversation/i;
const AUDIO_PLAYER = /Recording unavailable|Play recording|Pause recording|Seek to /i;
const ADVISORY = /not an official OET (?:score|result)/i;

// The lines of a page text that mention a recording.
export const recordingMentions = (text) => String(text ?? '').split(/\r?\n/).filter((line) => RECORDING.test(line)).map((line) => line.trim().slice(0, 160));

// Readiness band codes printed as they are stored. exam_ready / not_ready never occur in prose; the one-word codes do ("a
// strong opening"), so those count only as a whole line or right after "band".
export function rawBandCodes(text) {
  const body = String(text ?? '');
  const lines = body.split(/\r?\n/).map((line) => line.trim());
  return ['exam_ready', 'not_ready', 'developing', 'borderline', 'strong'].filter((code) => (code.includes('_')
    ? new RegExp(`\\b${code}\\b`, 'i').test(body)
    : lines.includes(code) || new RegExp(`\\b[Bb]and[:\\s]+${code}\\b`).test(body)));
}

/**
 * @param {any} input { sessions?, exam? }
 *   sessions  one entry per results page of a card or role-play: { id, overview, transcriptTab, banner }. overview = the page
 *             text once graded, before the transcript tab is opened; banner = this kind of page shows the submission banner
 *             (practice does, an exam card does not)
 *   exam      the exam results page text, or null
 * Returns { ok, problems }.
 */
export function resultsWordingVerdict(input) {
  const { sessions = [], exam = null } = input;
  const problems = [];
  for (const page of sessions) {
    const mention = recordingMentions(page.overview)[0];
    if (mention) problems.push(`${page.id}: the results page says "${mention}"`);
    if (page.banner && !SUBMISSION_BANNER.test(page.overview ?? '')) problems.push(`${page.id}: the submission banner does not say the transcript of the live conversation was saved`);
    if (!LIVE_COPY.test(`${page.overview ?? ''}\n${page.transcriptTab ?? ''}`)) problems.push(`${page.id}: nothing on the page says this was a live conversation`);
    if (AUDIO_PLAYER.test(page.transcriptTab ?? '')) problems.push(`${page.id}: the transcript tab still shows an audio player`);
  }
  if (exam !== null) {
    const mention = recordingMentions(exam)[0];
    if (mention) problems.push(`exam results: the page says "${mention}"`);
    const codes = rawBandCodes(exam);
    if (codes.length) problems.push(`exam results: the raw band code ${codes.join(', ')} is shown`);
    if (!ADVISORY.test(exam)) problems.push('exam results: no advisory sentence (not an official OET score or result)');
  }
  return { ok: problems.length === 0, problems };
}

// Nearest-rank percentile of an ascending list (index ceil(p n) - 1); null for an empty list. The epsilon keeps float noise
// (0.7 x 10 = 7.000000000000001) from rounding up a whole rank.
export function nearestRank(sorted, p) {
  return sorted.length ? sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(p * sorted.length - 1e-9) - 1))] : null;
}

// The harness's own navigations abort the AI Assistant hub's connect (a SignalR long-poll, not the product under
// test). SignalR words that abort differently depending on how far the connect had got, always ending in the
// browser's "Failed to fetch": all of them are noise when a navigation just happened.
const NAVIGATION_ABORT = /(Connection disconnected with error|Failed to start the (transport|connection)|Failed to complete negotiation with the server|\[AI Assistant\] Connection failed)[\s\S]*Failed to fetch/;
export function isNavigationAbortNoise(text) {
  return NAVIGATION_ABORT.test(String(text));
}
