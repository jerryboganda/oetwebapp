// Which provider(s) served a live-voice E2E run, and the conversation record attributed to them
// (used by speaking-live-voice-browser-e2e.mjs; the workflow copies this file next to it, so keep it self-contained).
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
