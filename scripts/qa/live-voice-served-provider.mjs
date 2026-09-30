// Which provider(s) served a live-voice E2E run, and the conversation record attributed to them
// (used by speaking-live-voice-browser-e2e.mjs; the workflow copies this file next to it, so keep it self-contained).
// It also holds the two pure helpers of the fault-injection runs (parseFault, recoveredAsRequested), at the end.
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

// Fault injection for the mid-session recovery runs (FAULT_DROP_AT_S / FAULT_STALL_AT_S, see the E2E script).
//
// Which fault a run asked for. Blank = off; DROP wins when both are set (one fault per run). Throws on a value that can
// never fire and on a run that cannot recover: the app never recovers a pinned provider (VOICE_PROVIDER), and a
// FAIL_PRIMARY run expects exactly one create call per provider, which a recovery (a further create call) breaks.
//   maxAtSeconds  the fault must fire before this many seconds of the conversation have passed
export function parseFault({ dropAt = '', stallAt = '', pinnedProvider = '', failPrimary = false, maxAtSeconds = Infinity }) {
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
  if (drop === null && stall === null) return { kind: null, atSeconds: null, stallIgnored: false };
  if (pinnedProvider) throw new Error('FAULT_DROP_AT_S / FAULT_STALL_AT_S need a blank VOICE_PROVIDER: a pinned provider never recovers, so the fault would only kill the session.');
  if (failPrimary) throw new Error('FAULT_DROP_AT_S / FAULT_STALL_AT_S cannot be combined with FAIL_PRIMARY: its failover check expects exactly one create call per provider and a recovery makes another.');
  return drop !== null
    ? { kind: 'drop', atSeconds: drop, stallIgnored: stall !== null }
    : { kind: 'stall', atSeconds: stall, stallIgnored: false };
}

// metrics.checks.recoveredAsRequested: null when no fault was requested; otherwise true only when the fault really fired,
// the panel reports at least one recovery, the conversation did not end in the error state, and the patient spoke again
// (an audible span or a transcript delta) after the recovery session was asked for.
//   fault       { kind, firedAt, recoveredAt }: firedAt null = the fault never fired; recoveredAt = the first provider
//               create call after it (null = none seen, then the fault time stands in)
//   recoveries  the faulted card's data-live-recoveries (null = the panel was never read)
//   errorShown  whether the panel showed its error alert at the last reading while the conversation was live (null = unknown)
//   patientAt   epoch ms of every patient audio span start and patient transcript delta of the run
export function recoveredAsRequested({ fault, recoveries, errorShown, patientAt }) {
  if (!fault?.kind) return null;
  if (!fault.firedAt) return false;
  const since = fault.recoveredAt ?? fault.firedAt;
  return (recoveries ?? 0) >= 1 && errorShown !== true && patientAt.some((t) => t > since);
}
