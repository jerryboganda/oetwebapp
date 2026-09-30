// Production browser E2E for the live AI patient (Gemini Live or OpenAI GPT-Live).
// Real Chromium at phone width, fake microphone playing a scripted candidate (once, no loop),
// real learner pages. MODE=practice: rules + consent -> prep -> live role-play
// -> submit -> result. MODE=exam: intro consent -> Card A -> Card B -> results.
// Measures, identically for both providers: provider-reported usage, audible patient speech
// spans (latency, barge-in, talk-over, silences), stability events (WebSocket close codes,
// RTCPeerConnection states), the SAVED transcripts exactly as the results page loads them
// (ordering + cross-card leak checks) and the patient's audio for a listening check.
// Provider selection: VOICE_PROVIDER pins one provider (the app never fails over then, so every check is strict).
// Blank = the server's candidates (configured primary first; health only filters). EXPECTED_PRIMARY (openai | gemini, blank = no assertion) is the
// provider the run should try first; it is ignored when VOICE_PROVIDER pins one (metrics.expectedPrimaryIgnored,
// and its check stays null). FAIL_PRIMARY=true answers that provider's create call with a 503 in the browser (the
// request never reaches the API, so only the client failover is exercised) and asserts the app fails over to the
// other one (exactly one call to each, per card).
// Which provider served is what each card's panel reports (data-live-provider): metrics.servedProvider. Transcripts,
// usage and the split check are attributed to it, never to "some data-channel event exists".
// Fault injection (mid-session recovery runs), blank = off: FAULT_DROP_AT_S=<s> kills the live provider connection from
// inside the page <s> seconds after the candidate microphone tape starts (Gemini: its WebSocket is closed; OpenAI: the
// 'oai-events' data channel is closed); FAULT_STALL_AT_S=<s> makes the provider go silent instead (every server message is
// swallowed until the app builds a new transport). DROP wins when both are set. Only the first live conversation is hit
// (practice, or exam Card A). Both are rejected with VOICE_PROVIDER (a pinned provider never recovers, so the run would only
// kill the session) and with FAIL_PRIMARY. Result: metrics.fault, metrics.recoveries (the panel's data-live-recoveries) and
// checks.recoveredAsRequested (>= 1 recovery on the panel, the patient spoke again after the recovery session was asked
// for, and the panel did not end in the error state).
// Run by .github/workflows/speaking-live-voice-prod-e2e.yml (never locally).
import { chromium, devices } from 'playwright';
import fs from 'node:fs';
import { installProbes } from './live-voice-browser-probes.mjs';
import { createServedRecord, parseFault, recoveredAsRequested } from './live-voice-served-provider.mjs';

const APP = process.env.APP_URL ?? 'https://app.oetwithdrhesham.co.uk';
const {
  QA_EMAIL, QA_PASSWORD, QA_DEVICE_ID = '', CARD_ID = '', CANDIDATE_WAV, CANDIDATE_TIMELINE = '',
  SPEAK_SECONDS = '', VOICE_PROVIDER = '', MODE = 'practice',
  SCRIPT_NAME = '', VOICE = '', SCRIPT_FILE = '',
  EXPECTED_PRIMARY = '', FAIL_PRIMARY = '',
  FAULT_DROP_AT_S = '', FAULT_STALL_AT_S = '',
} = process.env;
const CALL_OF = { openai: 'openai/offer', gemini: 'gemini/token' };
const failPrimary = FAIL_PRIMARY === 'true';
if (EXPECTED_PRIMARY && !CALL_OF[EXPECTED_PRIMARY]) throw new Error(`EXPECTED_PRIMARY must be openai or gemini, not "${EXPECTED_PRIMARY}".`);
if (failPrimary && (!EXPECTED_PRIMARY || VOICE_PROVIDER)) {
  throw new Error('FAIL_PRIMARY needs EXPECTED_PRIMARY and a blank VOICE_PROVIDER: a pinned provider never fails over.');
}
const primaryCall = EXPECTED_PRIMARY ? CALL_OF[EXPECTED_PRIMARY] : null;
const secondaryCall = EXPECTED_PRIMARY ? CALL_OF[EXPECTED_PRIMARY === 'openai' ? 'gemini' : 'openai'] : null;
const out = 'live-voice-e2e';
fs.mkdirSync(out, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
// A pinned provider is never "tried first" (the app makes one attempt and never fails over), so an expected primary has
// nothing to assert there: it is reported as ignored, not silently dropped.
const expectedPrimaryIgnored = Boolean(EXPECTED_PRIMARY && VOICE_PROVIDER);
if (expectedPrimaryIgnored) log(`EXPECTED_PRIMARY=${EXPECTED_PRIMARY} is ignored: the run is pinned to ${VOICE_PROVIDER}.`);
const timeline = CANDIDATE_TIMELINE && fs.existsSync(CANDIDATE_TIMELINE) ? JSON.parse(fs.readFileSync(CANDIDATE_TIMELINE, 'utf8')) : [];
const scriptText = SCRIPT_FILE && fs.existsSync(SCRIPT_FILE) ? fs.readFileSync(SCRIPT_FILE, 'utf8') : '';
// Practice mode submits after this many seconds of conversation. Blank = the candidate tape's last
// speech + 10 s. Capped at 280: the app's own 5:00 clock starts before the live indicator and
// auto-finalises the session, which would remove the Finish button.
const speakSeconds = Math.min(280, Number(SPEAK_SECONDS) || Math.ceil((timeline.at(-1)?.end ?? 100) + 10));
// Fault injection (see the header). Validated here, before any provider is billed. An exam card ends by itself at 5:00.
const fault = parseFault({
  dropAt: FAULT_DROP_AT_S, stallAt: FAULT_STALL_AT_S, pinnedProvider: VOICE_PROVIDER, failPrimary,
  maxAtSeconds: MODE === 'exam' ? 280 : speakSeconds,
});
if (fault.stallIgnored) log(`FAULT_STALL_AT_S=${FAULT_STALL_AT_S} is ignored: FAULT_DROP_AT_S wins (one fault per run).`);
// Rule of thumb, not a limit: the app notices a stall 20 s after the candidate stopped talking, then mints a new session
// and the patient has to speak again; a dropped link is noticed at once.
if (fault.kind && MODE !== 'exam' && fault.atSeconds + (fault.kind === 'stall' ? 40 : 15) > speakSeconds) {
  log(`WARNING: a ${fault.kind} at ${fault.atSeconds} s leaves little of the ${speakSeconds} s conversation for the app to recover in (recoveredAsRequested may be red for that reason alone). Fire it earlier or raise SPEAK_SECONDS.`);
}

const browser = await chromium.launch({
  args: [
    '--use-fake-ui-for-media-stream',
    '--use-fake-device-for-media-stream',
    // %noloop: play the tape once, then silence (a looping tape replays the greeting).
    `--use-file-for-fake-audio-capture=${CANDIDATE_WAV}%noloop`,
    '--autoplay-policy=no-user-gesture-required',
  ],
});
const context = await browser.newContext({ ...devices['Pixel 7'], permissions: ['microphone'] });
// A fresh CI browser is an unknown device (emailed-code verification); reuse
// the QA learner's already-approved device identity (lib/device-id.ts).
if (QA_DEVICE_ID) await context.addInitScript((id) => { try { localStorage.setItem('oet_device_id', id); } catch { /* cookie fallback */ } }, QA_DEVICE_ID);
// In-page probes (audio spans, patient recording, GPT-Live events, RTC / WebSocket state).
await context.addInitScript(installProbes, VOICE_PROVIDER);
// FAIL_PRIMARY: the primary provider's create call fails the way an outage does (503 + retryable:false), so the
// app must move on to the other provider by itself. The request never reaches the API: route.fulfill answers it in the
// browser (the page still counts it), so only the client failover is exercised. The server's breaker, health model and
// audit never see this failure.
if (failPrimary) {
  await context.route(new RegExp(`/realtime/sessions/[^/]+/${primaryCall}(?:[?#]|$)`), (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    return route.fulfill({
      status: 503,
      contentType: 'application/json',
      headers: { 'access-control-allow-origin': new URL(APP).origin, 'access-control-allow-credentials': 'true' },
      body: JSON.stringify({ code: 'live_voice_provider_unavailable', message: 'The realtime voice provider could not start this conversation. Please retry.', retryable: false }),
    });
  });
}

const page = await context.newPage();
const transcript = { candidate: '', patient: '' };
const stability = { providerErrors: [], socketsOpened: 0, socketCloses: [], sessionClosed: [], goAway: [], wsClose: [], rtcStates: [] };
// Everything the Gemini socket frames carried. Whether it counts is decided by who served (see servedProviders).
const gemini = { usage: [], audioChunks: [], audioAt: [], words: [], errors: [] };
// Every provider create call in request order, with how it ended. A 503 on one provider followed by a 200 on the
// other is a failover, not a browser error; usage and the ordering checks follow the provider that actually served.
// Bodies are never kept (the answer carries the SDP, the token URL carries the access token): status, the app's
// error code and hardStopAt only.
const providerCalls = [];
// When each create call was made (epoch ms), parallel to providerCalls: the first one after a fault is the recovery session.
const providerCallAt = [];
const providerAttempts = [];
const attemptOf = new Map();
page.on('request', (r) => {
  const m = r.method() === 'POST' && r.url().match(/\/realtime\/sessions\/[^/]+\/(openai\/offer|gemini\/token)/);
  if (!m) return;
  const attempt = { call: m[1], status: null, code: null, hardStopAt: null };
  providerCalls.push(m[1]);
  providerCallAt.push(Date.now());
  providerAttempts.push(attempt);
  attemptOf.set(r, attempt);
});
const attemptOk = (a) => a.status !== null && a.status >= 200 && a.status < 300;
const attemptFailed = (a) => a.status === 0 || (a.status !== null && a.status >= 500);
const servedCalls = () => providerAttempts.filter(attemptOk).map((a) => a.call);
const failoverSeen = () => providerAttempts.some((a, i) => attemptFailed(a) && providerAttempts.slice(i + 1).some((b) => attemptOk(b) && b.call !== a.call));
page.on('websocket', (ws) => {
  if (!ws.url().includes('generativelanguage.googleapis.com')) return;
  stability.socketsOpened += 1;
  log('Gemini Live socket opened');
  ws.on('framereceived', ({ payload }) => {
    const at = Date.now();
    try {
      const v = JSON.parse(typeof payload === 'string' ? payload : Buffer.from(payload).toString('utf8'));
      if (v.error) gemini.errors.push(JSON.stringify(v.error));
      if (v.goAway) stability.goAway.push({ at, ...v.goAway });
      if (v.usageMetadata) gemini.usage.push({ at, ...v.usageMetadata });
      const sc = v.serverContent;
      if (sc?.inputTranscription?.text) gemini.words.push({ at, who: 'candidate', text: sc.inputTranscription.text, startMs: null, endMs: null });
      if (sc?.outputTranscription?.text) gemini.words.push({ at, who: 'patient', text: sc.outputTranscription.text, startMs: null, endMs: null });
      for (const part of sc?.modelTurn?.parts ?? []) {
        if (part.inlineData?.data) { gemini.audioChunks.push(Buffer.from(part.inlineData.data, 'base64')); gemini.audioAt.push(at); }
      }
    } catch { /* non-JSON frame */ }
  });
  ws.on('close', () => { stability.socketCloses.push(Date.now()); log('Gemini Live socket closed'); });
});
// Every browser-side error of the run, for the "no socket / page errors" check.
const errors = { console: [], page: [], http: [], requestFailed: [] };
const redact = (t) => t.replace(/access_token=[^'" ]+/g, 'access_token=REDACTED').slice(0, 600);
// The harness's own navigations abort the open AI Assistant long-poll ("Failed to fetch");
// that is the harness, not the product.
let lastNavAt = 0;
const nav = (action) => { lastNavAt = Date.now(); return action(); };
const BENIGN_ON_NAVIGATION = /Connection disconnected with error 'TypeError: Failed to fetch'/;
page.on('console', (m) => {
  if (m.type() !== 'error') return;
  const text = redact(m.text());
  if (BENIGN_ON_NAVIGATION.test(text) && Date.now() - lastNavAt < 5_000) return;
  errors.console.push(text);
  log('console.error', text);
});
page.on('pageerror', (e) => { errors.page.push(redact(String(e))); log('pageerror', redact(String(e))); });
page.on('requestfailed', (r) => {
  const failedAttempt = attemptOf.get(r);
  if (failedAttempt && failedAttempt.status === null) failedAttempt.status = 0;
  const reason = r.failure()?.errorText ?? '';
  // Navigations and polls abandoned by a page change report ERR_ABORTED; not a failure.
  if (!/ERR_ABORTED/.test(reason)) errors.requestFailed.push(`${r.method()} ${redact(r.url())} ${reason}`);
});
const sessionIds = new Set();
const savedTranscripts = {};
const examCards = {};
let examDto = null;
page.on('response', async (r) => {
  const url = r.url();
  for (const m of url.matchAll(/\/sessions\/(sps_[a-f0-9]+)/g)) sessionIds.add(m[1]);
  const attempt = attemptOf.get(r.request());
  if (attempt) {
    attempt.status = r.status();
    const answer = await r.json().catch(() => null);
    if (typeof answer?.code === 'string') attempt.code = answer.code;
    if (typeof answer?.hardStopAt === 'string') attempt.hardStopAt = answer.hardStopAt;
  }
  if (r.status() >= 400 && /oetwithdrhesham|googleapis|openai/.test(url)) {
    // The app's own error code (e.g. live_voice_provider_unavailable) says why, without leaking a body.
    const code = /oetwithdrhesham/.test(url) ? (await r.json().catch(() => null))?.code : null;
    errors.http.push(`${r.status()} ${r.request().method()} ${redact(url)}${typeof code === 'string' ? ` ${code}` : ''}`);
  }
  if (/\/v1\/speaking\/exams\/[^/?]+$/.test(url) && r.ok()) {
    examDto = await r.json().catch(() => examDto);
    // A card's session id is null until the card is revealed: keep every one seen.
    for (const c of examDto?.cards ?? []) if (c?.sessionId) examCards[c.cardNumber] = c.sessionId;
  }
  // The saved transcript (what the grader reads) exactly as the results page loads it; a later
  // aborted or empty poll never replaces a good capture.
  const saved = url.match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)\/transcript(?:[?#]|$)/);
  if (saved && r.request().method() === 'GET' && r.ok()) {
    const body = await r.json().catch(() => null);
    if (body && (!findSegments(savedTranscripts[saved[1]]).length || findSegments(body).length)) savedTranscripts[saved[1]] = body;
  }
});
// Snapshot the page's audio spans and data-channel events every few seconds,
// keyed by document, so nothing is lost when a card or the results page loads.
const snapshots = {};
// What the live panel showed, per card ('roleplay' | 'cardA' | 'cardB'), sampled with every snapshot while that card is live
// (liveCard): the recovery count (data-live-recoveries, only present above zero) and whether it showed the error alert. The
// error state is not judged once the card is being saved (a failed save is another failure).
const panelSeen = {};
let liveCard = null;
const notePanel = (panel) => {
  if (!panel || !liveCard) return;
  const seen = (panelSeen[liveCard] ??= { recoveries: 0, error: null });
  seen.recoveries = Math.max(seen.recoveries, panel.recoveries);
  if (!/Saving conversation|Conversation saved/.test(panel.label)) seen.error = panel.alert;
};
const snapshot = async () => {
  const s = await page.evaluate(() => ({
    docId: window.__docId, events: window.__voiceEvents ?? [], mic: window.__micSpans ?? [], patient: window.__patientSpans ?? [],
    rtc: window.__rtcStates ?? [], ws: window.__wsdiag ?? [],
    fault: window.__lvFault ? { swallowed: window.__lvFault.swallowed } : null,
    panel: (() => {
      const el = document.querySelector('[data-testid="speaking-mic-indicator"]');
      if (!el) return null;
      const root = el.closest('[data-testid="speaking-conversation-panel"]') ?? el.parentElement;
      return {
        recoveries: Number(el.getAttribute('data-live-recoveries')) || 0,
        label: el.textContent ?? '',
        alert: Boolean(root?.querySelector('[role="alert"]')),
      };
    })(),
  })).catch(() => null);
  notePanel(s?.panel);
  if (s?.docId && (s.events.length || s.mic.length)) snapshots[s.docId] = s;
};
const snapshotTimer = setInterval(snapshot, 3_000);

// Who served (what each connected card's panel reports, see live-voice-served-provider.mjs) decides whose events,
// frames, usage and split check count. `metrics` is read lazily: it is created further down.
const { reportedProviders, servedProviders, openAiServed, geminiServed, openAiWords, applyServed } = createServedRecord({
  panels: () => [metrics.roleplayPanel, metrics.cardAPanel, metrics.cardBPanel],
  servedCalls, gemini, transcript, stability,
});

const readLive = async () => {
  const state = await page.evaluate(() => ({ events: window.__voiceEvents ?? [], patient: window.__patientSpans ?? [], micStartedAt: window.__micStartedAt }))
    .catch(() => ({ events: [], patient: [], micStartedAt: null }));
  applyServed(state.events);
  return state;
};

// Patient onset = start of an audible patient span (a >=400 ms gap from the previous one).
// Latency for line i = first onset after the line ends (within 15 s). Same for both providers.
function patientOnsets(patientRaw) {
  return merge(patientRaw).filter(([s, e]) => e - s >= 200).map(([s]) => s);
}
function latencies(micStartedAt, onsets) {
  if (!micStartedAt || !timeline.length) return [];
  const result = [];
  timeline.forEach((line, i) => {
    const end = micStartedAt + line.end * 1000;
    const onset = onsets.find((t) => t > end && t < end + 15_000);
    if (onset) result.push({ line: i + 1, ms: onset - end });
  });
  return result;
}
// Conversation behaviour from the two speech tracks (candidate mic, patient audio).
function merge(list) {
  return list.slice().sort((a, b) => a[0] - b[0]).reduce((acc, [s, e]) => {
    const last = acc.at(-1);
    if (last && s <= last[1] + 400) last[1] = Math.max(last[1], e);
    else acc.push([s, e]);
    return acc;
  }, []);
}
function conversation(micRaw, patientRaw) {
  const mic = merge(micRaw).filter(([s, e]) => e - s >= 300);
  const patient = merge(patientRaw).filter(([s, e]) => e - s >= 200);
  // Barge-in: the candidate starts while the patient has been talking >= 500 ms; the patient must stop.
  const bargeIns = mic.flatMap(([c0]) => {
    const p = patient.find(([p0, p1]) => p0 <= c0 - 500 && p1 > c0);
    return p ? [{ at: c0, patientStoppedAfterMs: p[1] - c0 }] : [];
  });
  // Talk-over: the patient starts speaking while the candidate is mid-utterance.
  const talkOver = patient.flatMap(([p0, p1]) => {
    const c = mic.find(([c0, c1]) => p0 > c0 + 300 && p0 < c1 - 300);
    if (!c) return [];
    // A patient span resuming <700 ms after one that was cut off is the tail of
    // an interrupted turn (barge-in latency); a short one is a backchannel.
    const tail = patient.some(([q0, q1]) => q1 < p0 && p0 - q1 < 700 && q1 > c[0] - 300 && q0 < c[0]);
    const kind = tail ? "barge-in tail" : p1 - p0 < 600 ? "backchannel" : "talk-over";
    return [{ at: p0, kind, overlapMs: Math.min(p1, c[1]) - p0, patientMs: p1 - p0 }];
  });
  // Silences: candidate gaps >= 15 s; the patient must still answer the next line.
  const silences = [];
  for (let i = 1; i < mic.length; i += 1) {
    const [gapStart, gapEnd] = [mic[i - 1][1], mic[i][0]];
    if (gapEnd - gapStart < 15_000) continue;
    const next = mic[i + 1]?.[0] ?? Infinity;
    const reply = patient.find(([p0]) => p0 > mic[i][1] && p0 < Math.min(next, mic[i][1] + 15_000));
    silences.push({
      gapMs: gapEnd - gapStart,
      patientSpokeDuringGapMs: patient.reduce((sum, [p0, p1]) => sum + Math.max(0, Math.min(p1, gapEnd) - Math.max(p0, gapStart)), 0),
      repliedToNextLineMs: reply ? reply[0] - mic[i][1] : null,
      nextLineEndAt: mic[i][1],
    });
  }
  const latency = mic.flatMap(([, e], i) => {
    const next = mic[i + 1]?.[0] ?? Infinity;
    const p = patient.find(([p0]) => p0 > e && p0 < Math.min(next, e + 15_000));
    return p ? [p[0] - e] : [];
  }).sort((a, b) => a - b);
  return {
    candidateUtterances: mic.length, patientUtterances: patient.length, bargeIns, talkOver, silences,
    latency: { samples: latency.length, medianMs: latency[Math.floor(latency.length / 2)] ?? null, p90Ms: latency[Math.floor(latency.length * 0.9)] ?? null },
  };
}
// Anything an AI assistant says that a real patient would not.
const OUT_OF_ROLE = /medical advice|as an ai\b|language model|\bi(?:'m| am) (?:an? )?(?:ai|artificial|virtual|assistant|chatbot)\b|not a (?:real )?(?:doctor|patient)\b|consult (?:a|your) (?:doctor|healthcare|medical)|i can(?:'t|not) (?:provide|give) (?:medical|a diagnosis)|role[- ]?play|simulation|delegat/i;
const outOfRole = (text) => text.split(/(?<=[.!?])\s+/).filter((s) => OUT_OF_ROLE.test(s));

// Segments of a saved transcript response, wherever the API nests them.
function findSegments(value, depth = 0) {
  if (depth > 4 || value == null) return [];
  if (typeof value === 'string') {
    try { return findSegments(JSON.parse(value), depth + 1); } catch { return []; }
  }
  if (Array.isArray(value)) {
    return value.length && value.every((s) => s && typeof s === 'object' && typeof s.text === 'string') ? value : [];
  }
  if (typeof value === 'object') {
    for (const key of Object.keys(value)) {
      const found = findSegments(value[key], depth + 1);
      if (found.length) return found;
    }
  }
  return [];
}
const speakerOf = (segment) => String(segment.speaker ?? segment.role ?? '').toLowerCase();
// The GPT-Live ordering defect: a short segment of one speaker (a backchannel such as "Uh.")
// between two segments of the other, where the second continues an unfinished sentence
// ("...How can I help you" / "Uh." / "today"). Same shape the app's isLateFragment merges.
function splitHazards(segments) {
  const words = (s) => s.text.trim().split(/\s+/).filter(Boolean).length;
  const hazards = [];
  for (let i = 1; i < segments.length - 1; i += 1) {
    const [before, mid, after] = [segments[i - 1], segments[i], segments[i + 1]];
    const close = typeof after.startMs !== 'number' || typeof before.endMs !== 'number' || after.startMs - before.endMs <= 2_000;
    if (speakerOf(before) !== speakerOf(mid) && speakerOf(after) === speakerOf(before) && words(mid) <= 3
      && /^\s*[a-z,;:.?!]/.test(after.text) && !/[.?!]\s*$/.test(before.text) && close) {
      hazards.push({ index: i, before: before.text.slice(-40), mid: mid.text, after: after.text.slice(0, 40) });
    }
  }
  return hazards;
}
// Cross-card leak suspects: words that appear only on one card's own screen text (not on the
// other card, not in the candidate script) but are spoken by the OTHER card's patient.
const STOP = new Set(('about above after again against also always another because been before being below between both cannot could does doing down during each either else even ever every from further have having here hers herself himself into itself just like many maybe might more most much must myself never only other ought ours ourselves over same should some such than that their theirs them themselves then there these they this those through under until very want well were what when where which while whom will with would your yours yourself yourselves please thank thanks doctor patient really okay yeah').split(' '));
const contentWords = (text) => new Set((text.toLowerCase().match(/[a-z]{4,}/g) ?? []).filter((w) => !STOP.has(w)));
function leakCheck(cardText, patientText, script) {
  const own = contentWords(script);
  const onScreen = { A: contentWords(cardText.A), B: contentWords(cardText.B) };
  const distinct = (x, y) => [...onScreen[x]].filter((w) => !onScreen[y].has(w) && !own.has(w));
  const said = { A: contentWords(patientText.A), B: contentWords(patientText.B) };
  return {
    aWordsSaidByPatientB: distinct('A', 'B').filter((w) => said.B.has(w)),
    bWordsSaidByPatientA: distinct('B', 'A').filter((w) => said.A.has(w)),
    distinctScreenWords: { A: distinct('A', 'B').length, B: distinct('B', 'A').length },
  };
}

function wav(pcm, rate = 24_000) {
  const header = Buffer.alloc(44);
  header.write('RIFF', 0); header.writeUInt32LE(36 + pcm.length, 4); header.write('WAVEfmt ', 8);
  header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24); header.writeUInt32LE(rate * 2, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34);
  header.write('data', 36); header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}

const shot = (name) => page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
const metrics = {
  mode: MODE, provider: VOICE_PROVIDER || 'primary', expectedPrimary: EXPECTED_PRIMARY || null, expectedPrimaryIgnored, failPrimary,
  cardId: CARD_ID, speakSeconds,
  // Fault injection: kind 'drop' | 'stall' | null (off); atSeconds after the candidate tape started; firedAt (epoch ms) and
  // provider are what the page really hit (null until it fired); recoveredAt = the first provider create call after it,
  // when the panel reports a recovery; swallowed = server messages hidden from the app by a stall; error only when no
  // live transport was found to hit.
  fault: { kind: fault.kind, atSeconds: fault.atSeconds, firedAt: null, provider: null, recoveredAt: null, swallowed: null, stallIgnored: fault.stallIgnored },
  script: SCRIPT_NAME, voice: VOICE, runId: process.env.GITHUB_RUN_ID ?? null, startedAt: new Date().toISOString(),
};
const cardText = {};
let failed = null;

// The fault fires once, in the first live conversation (practice, or exam Card A): from the candidate tape start (the
// microphone opening, the same origin the latency numbers use) + atSeconds.
let faultScheduled = false;
let faultTimer = null;
let faultFired = Promise.resolve();
async function fireFault(label) {
  const hint = metrics[`${label}Panel`]?.provider ?? null;
  const hit = await page.evaluate(({ kind, hint }) => {
    const f = window.__lvFault;
    const live = f?.live() ?? { gemini: 0, openai: 0 };
    const provider = hint && live[hint] ? hint : live.gemini ? 'gemini' : live.openai ? 'openai' : null;
    if (!f || !provider) return { provider: null, live };
    if (kind === 'stall') f.stallOn();
    else if (provider === 'gemini') f.dropGemini();
    else f.dropOpenAi();
    return { provider, live, at: Date.now() };
  }, { kind: fault.kind, hint }).catch((error) => ({ provider: null, error: String(error).slice(0, 300) }));
  if (!hit.provider) {
    metrics.fault.error = hit.error ?? `no live provider transport to ${fault.kind} ${JSON.stringify(hit.live)}`;
    log('FAULT NOT APPLIED:', metrics.fault.error);
    return;
  }
  metrics.fault.firedAt = hit.at;
  metrics.fault.provider = hit.provider;
  log(`FAULT ${fault.kind} applied to the ${hit.provider} transport`);
}
async function scheduleFault(label) {
  if (!fault.kind || faultScheduled) return;
  faultScheduled = true;
  const micStartedAt = (await page.evaluate(() => window.__micStartedAt).catch(() => null)) ?? Date.now();
  const wait = Math.max(0, micStartedAt + fault.atSeconds * 1000 - Date.now());
  log(`fault ${fault.kind}: firing in ${(wait / 1000).toFixed(1)} s (${fault.atSeconds} s after the microphone tape started)`);
  faultTimer = setTimeout(() => { faultFired = fireFault(label); }, wait);
}

async function startLive(label) {
  liveCard = label;
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 });
  const start = page.getByRole('button', { name: 'Start speaking' });
  // Auto-start may already be connecting (button shown but disabled); click only when needed.
  if (await start.isEnabled({ timeout: 2_000 }).catch(() => false)) await start.click({ timeout: 5_000 }).catch(() => undefined);
  const t0 = Date.now();
  // Fail fast, with the reason, when no provider can start the session (production 29 Sep: OpenAI 429). The app shows
  // this only after every candidate failed: "The live AI patient could not start" (older builds showed the server's
  // "The realtime voice provider could not start this conversation"). Deliberately narrow: the microphone ("Could not
  // start the microphone") and exam-page ("Could not start the discussion") errors are other failures, and reporting
  // them as "no provider could start" would point the reader at the wrong place.
  const connected = page.getByText(/Live — the patient is listening|Patient speaking/).first().waitFor({ timeout: 45_000 }).then(() => 'live');
  const refused = page.getByText(/(?:live AI patient|voice provider) could not start/i).first().waitFor({ timeout: 45_000 }).then(() => 'refused');
  const outcome = await Promise.race([connected, refused]).catch(() => 'timeout');
  connected.catch(() => undefined);
  refused.catch(() => undefined);
  if (outcome === 'refused') throw new Error(`${label}: no live voice provider could start the conversation (see metrics.providerAttempts and metrics.errors.http).`);
  if (outcome !== 'live') throw new Error(`${label}: live voice did not connect within 45 s.`);
  metrics[`${label}ConnectMs`] = Date.now() - t0;
  // What the panel says served this card (data attributes, not learner text): the source of metrics.servedProvider and
  // of every provider attribution below (see servedProviders). Also whether the microphone was released between cards.
  const indicator = page.getByTestId('speaking-mic-indicator');
  metrics[`${label}Panel`] = {
    provider: await indicator.getAttribute('data-live-provider').catch(() => null),
    failedOver: await indicator.getAttribute('data-live-failover').catch(() => null) === 'true',
  };
  metrics[`${label}MicStreams`] = await page.evaluate(() => (window.__micStreams ?? []).map((s) => s.getTracks().map((t) => t.readyState))).catch(() => null);
  log(`${label}: live voice connected`, JSON.stringify(metrics[`${label}Panel`]));
  await scheduleFault(label);
}

async function waitForGrade(label, minutes) {
  const deadline = Date.now() + minutes * 60_000;
  while (Date.now() < deadline) {
    // Let the page render (not its loading skeleton) before reading it.
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => undefined);
    const text = await page.locator('body').innerText();
    if (/Try grading again/i.test(text)) throw new Error(`${label}: grading failed on the results page.`);
    if (!/processing|being graded|analysing|Check again/i.test(text) && /\d{3}\s*\/\s*500|criteri/i.test(text)) return text;
    // The page polls by itself; act like a learner (no reloads, which also
    // abort in-flight requests and log spurious "Failed to fetch" errors).
    await page.waitForTimeout(30_000);
    await page.getByRole('button', { name: /check again/i }).click({ timeout: 1_000 }).catch(() => undefined);
  }
  throw new Error(`${label}: no graded result within ${minutes} minutes.`);
}

async function openAiUsage() {
  await page.waitForTimeout(2_000);
  const kept = await page.evaluate(() => JSON.parse(localStorage.getItem('__oai_usage') || '[]')).catch(() => []);
  const closed = kept.filter((e) => e.type === 'session.closed');
  return {
    sessionClosed: closed,
    finalBilledSeconds: closed.reduce((sum, e) => sum + (e.seconds ?? 0), 0),
    lastUsageUpdateSeconds: kept.filter((e) => e.type === 'session.usage.updated').at(-1)?.seconds ?? null,
    usageUpdates: kept.filter((e) => e.type === 'session.usage.updated').length,
  };
}

// The patient's audio exactly as audible, one webm per card / session (both providers).
async function savePatientAudio(name) {
  const audios = await page.evaluate(() => window.__patientAudios?.()).catch(() => null);
  (audios ?? []).forEach((rec, i) => {
    if (!rec?.audio) return;
    fs.writeFileSync(`${out}/${name}-${i + 1}.webm`, Buffer.from(rec.audio, 'base64'));
    fs.writeFileSync(`${out}/${name}-${i + 1}.json`, JSON.stringify({ startedAt: rec.startedAt }));
  });
}

// The results page loads the saved transcript itself (the response handler captures it); if that
// request was missed, one reload asks again. (A page-context fetch would carry no bearer token.)
async function openTranscript(id) {
  await page.getByText('Transcript', { exact: true }).first().click({ timeout: 10_000 }).catch(() => undefined);
  await page.waitForTimeout(2_000);
  if (!findSegments(savedTranscripts[id]).length) {
    await nav(() => page.reload());
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => undefined);
    await page.waitForTimeout(3_000);
  }
}

try {
  await page.goto(`${APP}/sign-in`);
  await page.locator('input[name="email"]').fill(QA_EMAIL);
  await page.locator('input[name="password"]').fill(QA_PASSWORD);
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL((u) => !u.pathname.startsWith('/sign-in'), { timeout: 60_000 });
  log('signed in');

  if (MODE === 'exam') {
    await page.goto(`${APP}/speaking/exam`);
    // A click before hydration is silently lost (no exam, no credits); retry
    // once. The button disables itself while an exam is being created.
    await page.waitForLoadState('networkidle', { timeout: 30_000 }).catch(() => undefined);
    const examUrl = /\/speaking\/exam\/[^/?]+(\?|$)/;
    for (let attempt = 0; attempt < 2 && !examUrl.test(page.url()); attempt += 1) {
      await page.getByRole('button', { name: 'Start AI exam' }).click({ timeout: 10_000 }).catch(() => undefined);
      await page.waitForURL(examUrl, { timeout: 45_000 }).catch(() => undefined);
    }
    if (!examUrl.test(page.url())) throw new Error('The AI exam did not start.');
    metrics.examId = page.url().match(/exam\/([^/?]+)/)?.[1];
    const consent = page.getByTestId('speaking-rules-consent');
    await consent.waitFor({ timeout: 60_000 });
    await shot('1-exam-intro');
    await consent.locator('input[type="checkbox"]').check();
    await consent.getByRole('button').last().click();
    for (const card of ['A', 'B']) {
      await page.getByRole('button', { name: /start the discussion now/i }).click({ timeout: 120_000 });
      await startLive(`card${card}`);
      cardText[card] = await page.locator('body').innerText().catch(() => '');
      await shot(`2-card-${card}-live`);
      // Each card ends automatically at 5:00; Card B's prep (or the results) follows.
      if (card === 'A') await page.getByRole('button', { name: /start the discussion now/i }).waitFor({ timeout: 7 * 60_000 });
      else await page.waitForURL(/\/speaking\/exam\/[^/]+\/results/, { timeout: 7 * 60_000 });
      liveCard = null;
      log(`card ${card} finished; provider calls so far: ${providerCalls.join(', ')}`);
      await shot(`3-after-card-${card}`);
    }
    metrics.providerCalls = providerCalls;
    // Usage follows the provider(s) that actually served a card, not the ones that were tried. The analysis scripts read
    // OpenAI's usage at the top level, so a mixed exam keeps it there and nests Gemini's under `gemini`.
    const geminiUsage = { usageMetadataFrames: gemini.usage.length, all: gemini.usage };
    metrics.providerUsage = openAiServed() && geminiServed()
      ? { ...(await openAiUsage()), gemini: geminiUsage }
      : openAiServed() ? await openAiUsage() : geminiUsage;
    await savePatientAudio('patient-audio');
    log('exam submitted', page.url());
    const text = await waitForGrade('exam', 25);
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 3000));
    metrics.examResult = text.match(/\d{3}\s*\/\s*500[^\n]*/g) ?? [];
    // After completion: the combined result survives a reload, and each card's
    // own results page and transcript load.
    const resultsUrl = page.url();
    await nav(() => page.reload());
    metrics.examResultAfterReload = (await waitForGrade('exam (reload)', 2)).match(/\d{3}\s*\/\s*500[^\n]*/g) ?? [];
    metrics.cards = [];
    // The two cards of this exam (any other session id seen in a URL is not one of them).
    for (const id of examCards[1] && examCards[2] ? [examCards[1], examCards[2]] : sessionIds) {
      await nav(() => page.goto(`${APP}/speaking/sessions/${id}/results`));
      const cardResult = await waitForGrade(`card ${id}`, 5);
      await openTranscript(id);
      const body = await page.locator('body').innerText();
      await shot(`6-card-${id}-transcript`);
      metrics.cards.push({ sessionId: id, score: cardResult.match(/\d{3}\s*\/\s*500/)?.[0] ?? null, transcriptChars: body.length, transcriptShown: /doctor smith|how can i help|think about/i.test(body) });
    }
    metrics.examDto = examDto;
    await nav(() => page.goto(resultsUrl));
  } else {
    await page.goto(`${APP}/speaking/roleplay/${CARD_ID}`);
    const consent = page.getByTestId('speaking-rules-consent');
    await consent.waitFor({ timeout: 60_000 });
    await shot('1-rules-consent');
    await consent.locator('input[type="checkbox"]').check();
    await consent.getByRole('button').last().click();
    await page.waitForURL(/\/speaking\/sessions\/[^/]+\/prep/, { timeout: 60_000 });
    await shot('2-prep');
    log('prep', page.url());
    await page.getByRole('button', { name: 'Start speaking now' }).click();
    await page.waitForURL(/\/speaking\/sessions\/[^/?]+(\?|$)/, { timeout: 60_000 });
    metrics.sessionId = page.url().match(/sessions\/([^/?]+)/)?.[1];
    await startLive('roleplay');
    cardText.practice = await page.locator('body').innerText().catch(() => '');
    await shot('3-active-live');
    await page.waitForTimeout(speakSeconds * 1000);
    await shot('4-active-after-conversation');
    const before = await readLive();
    metrics.micStartedAt = before.micStartedAt;
    await snapshot();
    liveCard = null;
    log('provider calls:', providerCalls.join(', ') || '(none)', '| data-channel events:', before.events.length);
    if (VOICE_PROVIDER && !providerCalls.includes(VOICE_PROVIDER === 'openai' ? 'openai/offer' : 'gemini/token')) {
      throw new Error(`Expected the ${VOICE_PROVIDER} provider, saw: ${providerCalls.join(', ') || 'none'}`);
    }
    if (stability.providerErrors.length) throw new Error(`Provider reported: ${stability.providerErrors[0]}`);
    if (!transcript.patient.trim()) throw new Error('The AI patient never spoke.');
    await savePatientAudio('patient-audio');
    // The app auto-finalises at 5:00: a session that outlasted it is already on the results page.
    const finish = page.getByRole('button', { name: 'Finish & submit' });
    if (!/\/results/.test(page.url()) && await finish.isVisible().catch(() => false)) {
      await finish.click();
      await page.getByRole('button', { name: 'Submit now' }).click();
    }
    await page.waitForURL(/\/results/, { timeout: 90_000 });
    log('submitted', page.url());
    // The results page is a new document: the live events were read above.
    const lat = latencies(before.micStartedAt, patientOnsets(before.patient));
    const sorted = lat.map((l) => l.ms).sort((a, b) => a - b);
    metrics.latency = {
      measuredTurns: lat.length,
      scriptedLines: timeline.length,
      medianMs: sorted[Math.floor(sorted.length / 2)] ?? null,
      p90Ms: sorted[Math.floor(sorted.length * 0.9)] ?? null,
      perLine: lat,
    };
    metrics.providerUsage = openAiServed()
      ? await openAiUsage()
      : { usageMetadataFrames: gemini.usage.length, last: gemini.usage.at(-1) ?? null, all: gemini.usage };
    log('METRICS', JSON.stringify({ ...metrics, providerUsage: { ...metrics.providerUsage, all: undefined } }));
    const text = await waitForGrade('role-play', 12);
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 2000));
    await openTranscript(metrics.sessionId);
    await shot('6-transcript');
  }
} catch (error) {
  failed = error;
  await shot('failure').catch(() => undefined);
} finally {
  // A fault that has not fired by now never will; one that is firing right now settles first.
  clearTimeout(faultTimer);
  await faultFired;
  await snapshot().catch(() => undefined);
  clearInterval(snapshotTimer);
  const docs = Object.values(snapshots);
  const events = docs.flatMap((d) => d.events);
  applyServed(events);
  stability.wsClose = docs.flatMap((d) => d.ws ?? []);
  stability.rtcStates = docs.flatMap((d) => d.rtc ?? []);
  metrics.conversation = conversation(docs.flatMap((d) => d.mic), docs.flatMap((d) => d.patient));
  // Timed words + speech spans, to read what was said at each barge-in / overlap. start/end are
  // the provider's own timeline (GPT-Live start_ms/end_ms), kept to calibrate transcript ordering. Words are the
  // serving provider(s)' own (OpenAI's events, Gemini's frames); eventTypes and speechEvents stay raw diagnostics.
  fs.writeFileSync(`${out}/timeline-events.json`, JSON.stringify({
    words: [...(openAiServed() ? openAiWords(events) : []), ...(geminiServed() ? gemini.words : [])].sort((a, b) => a.at - b.at),
    eventTypes: events.reduce((acc, e) => ({ ...acc, [e.type]: (acc[e.type] ?? 0) + 1 }), {}),
    speechEvents: events.filter((e) => /speech|interrupt|cancel|turn/i.test(e.type)).map((e) => ({ at: e.__at, type: e.type })),
    candidateSpans: merge(docs.flatMap((d) => d.mic)),
    patientSpans: merge(docs.flatMap((d) => d.patient)),
  }));
  // Saved transcripts as the learner's results page received them: the ordering check. A failure in
  // this analysis must never lose the run's other artifacts.
  const expected = MODE === 'exam'
    ? ([examCards[1], examCards[2]].filter(Boolean).length === 2 ? [examCards[1], examCards[2]] : [...sessionIds])
    : [metrics.sessionId].filter(Boolean);
  metrics.savedTranscripts = {};
  try {
    for (const [id, body] of Object.entries(savedTranscripts)) fs.writeFileSync(`${out}/saved-transcript-${id}.json`, JSON.stringify(body, null, 2));
    for (const id of expected) {
      const segments = findSegments(savedTranscripts[id]);
      metrics.savedTranscripts[id] = { segments: segments.length, splitHazards: segments.length ? splitHazards(segments) : null };
    }
    // Informational only: a bag-of-words suspect list to read alongside the two transcripts.
    if (MODE === 'exam' && expected.length === 2 && cardText.A && cardText.B) {
      const patientText = Object.fromEntries(['A', 'B'].map((k, i) => [k, findSegments(savedTranscripts[expected[i]])
        .filter((s) => speakerOf(s) === 'patient').map((s) => s.text).join(' ')]));
      metrics.leakCheck = leakCheck(cardText, patientText, scriptText);
    }
    if (gemini.audioChunks.length) fs.writeFileSync(`${out}/patient-audio-raw.wav`, wav(Buffer.concat(gemini.audioChunks)));
  } catch (error) {
    metrics.analysisError = String(error?.stack ?? error).slice(0, 800);
    log('analysis error', metrics.analysisError);
  }
  metrics.outOfRole = outOfRole(transcript.patient);
  metrics.errors = errors;
  metrics.providerCalls = providerCalls;
  metrics.providerAttempts = providerAttempts;
  const failoverObserved = failoverSeen();
  metrics.failoverObserved = failoverObserved;
  metrics.hardStopAt = providerAttempts.find((a) => a.hardStopAt)?.hardStopAt ?? null;
  // The provider whose link went live, as each connected card's panel reported it: one name, 'mixed' when an exam's
  // cards were served by different providers, null when none reported (nothing went live, or a build that predates
  // data-live-provider). providerCalls / providerAttempts still list every create call, failed ones included.
  const reported = [...new Set(reportedProviders())];
  metrics.servedProvider = reported.length === 0 ? null : reported.length === 1 ? reported[0] : 'mixed';
  const served = servedProviders();
  const openAiEvents = openAiServed() ? events : [];
  // A failed provider create call is expected, not a browser error, only in an unpinned run that then failed over.
  // A pinned run (VOICE_PROVIDER) never fails over, so any failure there stays a red check.
  const failoverNoise = (line) => !VOICE_PROVIDER && failoverObserved
    && /^(?:5\d\d POST|POST) \S+\/realtime\/sessions\/[^/\s]+\/(?:openai\/offer|gemini\/token)(?:\s|$)/.test(line);
  const secondaryName = EXPECTED_PRIMARY === 'openai' ? 'gemini' : 'openai';
  const panels = MODE === 'exam' ? [metrics.cardAPanel, metrics.cardBPanel] : [metrics.roleplayPanel];
  const c = metrics.conversation;
  const closedAt = (from) => openAiEvents.some((e) => e.type === 'session.closed' && e.__at >= from && e.__at - from < 20_000)
    || stability.socketCloses.some((t) => t >= from && t - from < 20_000);
  // Mid-session recovery, as each card's panel reported it (recoveries: null = the panel was never read; exam: the total,
  // with recoveriesA / recoveriesB per card). The fault hit the first live conversation only, so that card's panel decides.
  const counted = Object.values(panelSeen);
  metrics.recoveries = counted.length ? counted.reduce((sum, p) => sum + p.recoveries, 0) : null;
  if (MODE === 'exam') {
    metrics.recoveriesA = panelSeen.cardA?.recoveries ?? null;
    metrics.recoveriesB = panelSeen.cardB?.recoveries ?? null;
  }
  const faultedPanel = panelSeen[MODE === 'exam' ? 'cardA' : 'roleplay'];
  const faultMetrics = metrics.fault;
  // The recovery session is the first create call after the fault, but only when the panel reports a recovery (without one
  // the next call is something else: an exam's Card B).
  faultMetrics.recoveredAt = faultMetrics.firedAt && (faultedPanel?.recoveries ?? 0) >= 1
    ? (providerCallAt.find((t) => t > faultMetrics.firedAt) ?? null)
    : null;
  faultMetrics.swallowed = fault.kind ? Math.max(0, ...docs.map((d) => d.fault?.swallowed ?? 0)) : null;
  // Every moment the patient produced something: the start of each audible span, each transcript delta. Judged from the
  // recovery session's creation on, so what the old (dropped or stalled) transport still carried cannot count.
  const patientAt = [
    ...docs.flatMap((d) => d.patient).map(([start]) => start),
    ...gemini.words.filter((w) => w.who === 'patient').map((w) => w.at),
    ...events.filter((e) => e.type === 'session.output_transcript.delta').map((e) => e.__at),
  ];
  metrics.checks = {
    // The first provider the app tried is the one this run expected (an unpinned run with EXPECTED_PRIMARY set). Null
    // when nothing was expected, and when VOICE_PROVIDER pins one: then EXPECTED_PRIMARY is ignored
    // (metrics.expectedPrimaryIgnored), because a pinned run makes one attempt and never "tries first".
    primaryProviderIsExpected: !VOICE_PROVIDER && EXPECTED_PRIMARY ? providerCalls[0] === primaryCall : null,
    // A pinned run is served by the provider it pinned, in exam mode too (the page could lose ?voiceProvider=).
    pinnedProviderServed: VOICE_PROVIDER && reported.length ? reported.every((p) => p === VOICE_PROVIDER) : null,
    // FAIL_PRIMARY: per card exactly one call to the failed primary, then exactly one to the other provider, and
    // the panel reports the other provider as serving after a failover.
    failoverAsRequested: failPrimary
      ? failoverObserved
        && JSON.stringify(providerCalls) === JSON.stringify(Array.from({ length: panels.length }, () => [primaryCall, secondaryCall]).flat())
        && panels.every((p) => p?.provider === secondaryName && p.failedOver === true)
      : null,
    // FAULT_DROP_AT_S / FAULT_STALL_AT_S: the fault fired, the faulted card's panel reports >= 1 recovery, the patient spoke
    // again after the recovery session was asked for, and the panel did not end in the error state. Null without a fault.
    recoveredAsRequested: recoveredAsRequested({
      fault: faultMetrics, recoveries: faultedPanel?.recoveries ?? null, errorShown: faultedPanel?.error ?? null, patientAt,
    }),
    // Exam: each card carries its slot letter, never a printed source-card number (both cards can print the same one).
    // innerText is uppercased by CSS, hence /i.
    cardLabelsBySlot: MODE === 'exam' && cardText.A && cardText.B
      ? /role-play card a\b/i.test(cardText.A) && /role-play card b\b/i.test(cardText.B)
      : null,
    // Median stop <= 1.5 s and worst <= 2.5 s (provider VAD needs ~0.5 s of speech to react).
    bargeInPatientStops: c.bargeIns.length
      ? c.bargeIns.map((b) => b.patientStoppedAfterMs).sort((a, b) => a - b)[Math.floor(c.bargeIns.length / 2)] <= 1_500
        && c.bargeIns.every((b) => b.patientStoppedAfterMs <= 2_500)
      : null,
    noPatientTalkOver: c.talkOver.every((t) => t.kind !== "talk-over"),
    // A line that runs into the card's 5:00 end cannot be answered; skip it.
    survivesSilences: c.silences.length
      ? c.silences.every((s) => s.repliedToNextLineMs !== null || closedAt(s.nextLineEndAt))
        && !stability.providerErrors.length
      : null,
    staysInRole: metrics.outOfRole.length === 0,
    // Placement status 404s by design for learners outside its beta; "Failed to load
    // resource" console lines duplicate the HTTP errors counted here.
    noBrowserErrors: !errors.console.some((m) => !/^Failed to load resource/.test(m)) && !errors.page.length
      && !errors.http.some((h) => !h.includes('/v1/placement/status') && !failoverNoise(h))
      && !errors.requestFailed.some((h) => !failoverNoise(h)),
    // Every card's saved transcript was captured, and (GPT-Live only, judged on the provider that served: its
    // transcription lag is the #257 defect) has no split sentence.
    savedTranscriptsCaptured: expected.length > 0 && expected.every((id) => metrics.savedTranscripts[id]?.segments > 0),
    noSplitSentences: served.length > 0 && served.every((p) => p === 'openai')
      ? expected.length > 0 && expected.every((id) => metrics.savedTranscripts[id]?.splitHazards?.length === 0)
      : null,
    // Cross-card leaks are judged by reading both transcripts; metrics.leakCheck only lists suspects.
    noCrossCardLeak: null,
  };
  log('CHECKS', JSON.stringify(metrics.checks), 'CONVERSATION', JSON.stringify(c));
  metrics.stability = stability;
  fs.writeFileSync(`${out}/transcript.json`, JSON.stringify(transcript, null, 2));
  fs.writeFileSync(`${out}/card-text.json`, JSON.stringify(cardText, null, 2));
  fs.writeFileSync(`${out}/metrics.json`, JSON.stringify(metrics, null, 2));
  log('CANDIDATE (as heard by the provider):', transcript.candidate.trim().slice(0, 4000));
  log('PATIENT (AI):', transcript.patient.trim().slice(0, 6000));
  await browser.close();
}
const failedChecks = Object.entries(metrics.checks ?? {}).filter(([, ok]) => ok === false).map(([name]) => name);
if (failed || failedChecks.length) {
  console.error(failed ?? `Checks failed: ${failedChecks.join(', ')}`);
  process.exit(1);
}
