// Production browser E2E for the live AI patient (Gemini Live or OpenAI GPT-Live).
// Real Chromium at phone width, fake microphone playing a scripted candidate (once, no loop),
// real learner pages. MODE=practice: rules + consent -> prep -> live role-play
// -> submit -> result. MODE=exam: intro consent -> Card A -> Card B -> results.
// Measures, identically for both providers: provider-reported usage, audible patient speech
// spans (latency, barge-in, talk-over, silences), stability events (WebSocket close codes,
// RTCPeerConnection states), the SAVED transcripts exactly as the results page loads them
// (ordering + cross-card leak checks) and the patient's audio for a listening check.
// Provider selection: VOICE_PROVIDER asks the server to pin one provider (the app never fails over then, so every check is
// strict). The server honours it only for a QA learner with an enabled feature flag speaking_live_voice_pin:<learner user id>;
// for anyone else it answers pinned:false and the normal order. The run fails fast, naming that flag, when the server ignored
// the pin, and checks.pinHonoured judges the server's answer (the provider that happened to serve proves nothing).
// Blank = the server's candidates (configured primary first; health only filters). EXPECTED_PRIMARY (openai | gemini, blank = no assertion) is the
// provider the run should try first; it is ignored when VOICE_PROVIDER pins one (metrics.expectedPrimaryIgnored,
// and its check stays null). FAIL_PRIMARY=true answers that provider's create call with a 503 in the browser (the
// request never reaches the API, so only the client failover is exercised) and asserts the app fails over to the
// other one (exactly the create calls failoverCallsOk expects, per card, recoveries and reloads included).
// Which provider served is what each card's panel reports (data-live-provider): metrics.servedProvider. Transcripts,
// usage and the split check are attributed to it, never to "some data-channel event exists".
// Fault injection (mid-session runs), blank = off: FAULT_DROP_AT_S=<s> kills the live provider connection from
// inside the page <s> seconds after the candidate microphone tape starts (Gemini: its WebSocket is closed; OpenAI: the
// 'oai-events' data channel is closed); FAULT_STALL_AT_S=<s> makes the provider go silent instead (every server message is
// swallowed until the app builds a new transport); FAULT_RELOAD_AT_S=<s> refreshes the page (the learner presses F5), presses
// Start speaking when the panel does not reconnect by itself and records what the product did (metrics.reload). DROP wins over
// STALL over RELOAD. Only the first live conversation is hit (practice, or exam Card A). Drop and stall are rejected with
// VOICE_PROVIDER (a pinned provider never recovers, so the run would only kill the session); a reload is not. All three work
// with FAIL_PRIMARY (then the fault hits the fallback). Result: metrics.fault, metrics.recoveries (the panel's
// data-live-recoveries) and checks.recoveredAsRequested (>= 1 recovery on the panel, the patient spoke again after the recovery
// session was asked for, and the panel did not end in the error state; null for a reload), checks.reloadResumed and
// checks.reloadKeepsTranscript (the words said before the refresh are in the saved transcript).
// VERIFY_CREDITS=true reads the QA learner's AI credits through the page's own bearer before anything is spent (and refuses to
// start when the account is funded another way or cannot fund the run), at each card's hold, after grading and at the end:
// exactly 4 (exam) or 2 (practice) credits, exactly the expected ledger rows, no refund (checks.creditsDeductedOnce); it then
// checks the History page (checks.historyListsExam). GRADE_RETRY=true asks for the grade again after grading and expects a
// no-op (checks.gradeRetryIdempotent).
// Saved transcripts are judged as the grader reads them: metrics.savedTranscripts[id].quality (Q1-Q11) and .wire (against what
// the provider sent), summarised in checks.transcriptQuality / transcriptsMatchWire / candidateLabelsAreTheTape / noCrossCardLeak.
// Run by .github/workflows/speaking-live-voice-prod-e2e.yml (never locally).
import { chromium, devices } from 'playwright';
import fs from 'node:fs';
import { installProbes } from './live-voice-browser-probes.mjs';
import {
  createServedRecord, creditPools, creditPreflight, creditRowDelta, creditVerdict, failoverCallsOk, gradeRetryVerdict, historyVerdict,
  isNavigationAbortNoise, learnerIdFromBearer, nearestRank, parseFault, pinHonoured, pinIgnoredMessage, recoveredAsRequested,
  resultsWordingVerdict,
} from './live-voice-served-provider.mjs';
import { expectedLines, repeatedPatientSegments, scriptLines, speakerMatch, tokens, transcriptQuality, transcriptVerdict } from './live-voice-transcript-quality.mjs';

const APP = process.env.APP_URL ?? 'https://app.oetwithdrhesham.co.uk';
const {
  QA_EMAIL, QA_PASSWORD, QA_DEVICE_ID = '', CARD_ID = '', CANDIDATE_WAV, CANDIDATE_TIMELINE = '',
  SPEAK_SECONDS = '', VOICE_PROVIDER = '', MODE = 'practice',
  SCRIPT_NAME = '', VOICE = '', SCRIPT_FILE = '',
  EXPECTED_PRIMARY = '', FAIL_PRIMARY = '',
  FAULT_DROP_AT_S = '', FAULT_STALL_AT_S = '', FAULT_RELOAD_AT_S = '',
  VERIFY_CREDITS = '', GRADE_RETRY = '',
} = process.env;
const CALL_OF = { openai: 'openai/offer', gemini: 'gemini/token' };
const failPrimary = FAIL_PRIMARY === 'true';
const verifyCredits = VERIFY_CREDITS === 'true';
const gradeRetry = GRADE_RETRY === 'true';
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
  dropAt: FAULT_DROP_AT_S, stallAt: FAULT_STALL_AT_S, reloadAt: FAULT_RELOAD_AT_S, pinnedProvider: VOICE_PROVIDER,
  maxAtSeconds: MODE === 'exam' ? 280 : speakSeconds,
});
if (fault.stallIgnored) log(`FAULT_STALL_AT_S=${FAULT_STALL_AT_S} is ignored: FAULT_DROP_AT_S wins (one fault per run).`);
if (fault.reloadIgnored) log(`FAULT_RELOAD_AT_S=${FAULT_RELOAD_AT_S} is ignored: FAULT_${fault.kind === 'drop' ? 'DROP' : 'STALL'}_AT_S wins (one fault per run).`);
// Rule of thumb, not a limit: the app notices a stall 20 s after the candidate stopped talking, then mints a new session
// and the patient has to speak again; a dropped link is noticed at once; a refresh needs the page and the provider back.
if (fault.kind && MODE !== 'exam' && fault.atSeconds + (fault.kind === 'stall' ? 40 : fault.kind === 'reload' ? 30 : 15) > speakSeconds) {
  log(`WARNING: a ${fault.kind} at ${fault.atSeconds} s leaves little of the ${speakSeconds} s conversation for the app to recover in (the fault's own checks may be red for that reason alone). Fire it earlier or raise SPEAK_SECONDS.`);
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
  const socket = stability.socketsOpened;
  log('Gemini Live socket opened');
  ws.on('framereceived', ({ payload }) => {
    const at = Date.now();
    try {
      const v = JSON.parse(typeof payload === 'string' ? payload : Buffer.from(payload).toString('utf8'));
      if (v.error) gemini.errors.push(JSON.stringify(v.error));
      if (v.goAway) stability.goAway.push({ at, ...v.goAway });
      // socket = which Gemini connection billed it (1 = the first one opened), so a recovery run can be costed per session.
      if (v.usageMetadata) gemini.usage.push({ at, socket, ...v.usageMetadata });
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
// The harness's own navigations abort the AI Assistant hub's connect or open long-poll ("Failed to fetch");
// that is the harness, not the product (see isNavigationAbortNoise).
let lastNavAt = 0;
const nav = (action) => { lastNavAt = Date.now(); return action(); };
page.on('console', (m) => {
  if (m.type() !== 'error') return;
  const text = redact(m.text());
  if (isNavigationAbortNoise(text) && Date.now() - lastNavAt < 5_000) return;
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
// The page's own answers, kept without any request of ours: the combined exam result, each session's grading state (with the
// kind of input it handed in and whether it was a free sample), its score and grader, and the preflight answers that asked for
// a provider (the QA pin).
let examResults = null;
const sessionResults = {};
const sessionDetails = {};
const aiAssessments = {};
const preflights = [];
// The QA session's bearer token and device id, taken from the page's own API calls: the credit, grading and History reads
// below go through the page itself (same origin, its cookies), so they need no password and never touch the live card.
// aiAssessCalls counts the POST .../ai-assess calls the page made per session (the results page kicks one per card).
let bearer = null;
let deviceId = null;
const aiAssessCalls = {};
page.on('request', (r) => {
  if (!r.url().includes('/api/backend/v1/')) return;
  r.headerValue('authorization').then((v) => { if (v) bearer = v; }, () => undefined);
  r.headerValue('x-oet-device-id').then((v) => { if (v) deviceId = v; }, () => undefined);
  const assess = r.method() === 'POST' && r.url().match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)\/ai-assess(?:[?#]|$)/);
  if (assess) aiAssessCalls[assess[1]] = (aiAssessCalls[assess[1]] ?? 0) + 1;
});
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
  if (r.request().method() === 'GET') {
    // A preflight that asked for a provider: the server's own "pinned" is what counts. Answers that were not 200 are kept too,
    // so a rate-limited preflight is not mistaken for a refused pin.
    const preflight = url.match(/\/v1\/speaking\/realtime\/sessions\/[^/?]+\/preflight\?(?:[^#]*&)?provider=([a-z]+)/i);
    if (preflight) {
      const answer = r.ok() ? await r.json().catch(() => null) : null;
      preflights.push({ requested: preflight[1].toLowerCase(), status: r.status(), pinned: answer?.pinned === true, provider: answer?.provider ?? null });
    }
  }
  if (r.request().method() === 'GET' && r.ok()) {
    const answer = () => r.json().catch(() => null);
    const sessionState = url.match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)\/results(?:[?#]|$)/);
    const sessionDetail = url.match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)(?:[?#]|$)/);
    const assessment = url.match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)\/ai-assessment(?:[?#]|$)/);
    // The results page reads the AI score from the flat .../assessments answer ({ ai: { assessmentId, provider, modelId, ... } }).
    const dual = url.match(/\/v1\/speaking\/sessions\/(sps_[a-f0-9]+)\/assessments(?:[?#]|$)/);
    if (/\/v1\/speaking\/exams\/[^/?]+\/results(?:[?#]|$)/.test(url)) examResults = (await answer()) ?? examResults;
    else if (sessionState) { const body = await answer(); if (body) sessionResults[sessionState[1]] = body; }
    else if (sessionDetail) { const body = await answer(); if (body) sessionDetails[sessionDetail[1]] = body; }
    else if (assessment) { const body = await answer(); if (body) aiAssessments[assessment[1]] = body; }
    else if (dual) { const body = await answer(); if (body?.ai) aiAssessments[dual[1]] = body.ai; }
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
  const seen = (panelSeen[liveCard] ??= { recoveries: 0, error: null, provider: null });
  seen.recoveries = Math.max(seen.recoveries, panel.recoveries);
  // The provider last shown while this card was live: after a recovery that switched providers it is the one that saved.
  seen.provider = panel.provider ?? seen.provider;
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
        provider: el.getAttribute('data-live-provider'),
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
    // samples = how many; samplesMs = the raw values (ascending), so several runs can be pooled for one percentile (a single run
    // has only ~11 candidate lines per card). p95Ms is nearest-rank; the older median and p90 keep their definitions.
    latency: {
      samples: latency.length, samplesMs: latency,
      medianMs: latency[Math.floor(latency.length / 2)] ?? null, p90Ms: latency[Math.floor(latency.length * 0.9)] ?? null,
      p95Ms: nearestRank(latency, 0.95),
    },
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
  // Fault injection: kind 'drop' | 'stall' | 'reload' | null (off); atSeconds after the candidate tape started; firedAt (epoch ms)
  // and provider are what the page really hit (null until it fired); tapeStartedAt = when that tape started (epoch ms);
  // recoveredAt = the first provider create call after it, when the panel reports a recovery; swallowed = server messages hidden
  // from the app by a stall; error only when nothing could be hit.
  fault: {
    kind: fault.kind, atSeconds: fault.atSeconds, firedAt: null, provider: null, recoveredAt: null, swallowed: null,
    stallIgnored: fault.stallIgnored, reloadIgnored: fault.reloadIgnored, tapeStartedAt: null,
  },
  // A page reload: whether the product reconnected by itself, how long until the patient was back, which provider it came back on.
  reload: null,
  // VERIFY_CREDITS: errors = reads that failed after the run began; pools = the balance at each reading; verdict = creditVerdict.
  credits: verifyCredits ? { errors: [] } : null,
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
  if (fault.kind === 'reload') return fireReload(label);
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

const LIVE_TEXT = /Live — the patient is listening|Patient speaking/;
// FAULT_RELOAD_AT_S: the learner refreshes the page mid-card. The new document has no user activation, so the panel reconnects
// by itself only if the browser carries it over; otherwise the learner presses Start speaking. Both are handled, and what the
// product did is the result (metrics.reload). Whether the words said before the refresh survive in the saved transcript is
// judged later (checks.reloadKeepsTranscript).
async function fireReload(label) {
  const provider = metrics[`${label}Panel`]?.provider ?? null;
  await snapshot(); // the new document starts empty: keep what the old one heard
  const at = Date.now();
  try {
    await nav(() => page.reload());
  } catch (error) {
    metrics.fault.error = String(error).slice(0, 300);
    log('FAULT NOT APPLIED:', metrics.fault.error);
    return;
  }
  metrics.fault.firedAt = at;
  metrics.fault.provider = provider;
  log(`FAULT reload applied to the ${provider ?? 'unknown'} conversation`);
  const live = page.getByText(LIVE_TEXT).first();
  // What a learner presses when the panel does not reconnect: Start speaking, or Retry connection after a failed preflight
  // (then Start speaking again). At most three presses, a few seconds apart.
  const press = page.getByRole('button', { name: /^(?:Start speaking|Retry connection)$/ });
  const giveUpAt = Date.now() + 60_000;
  let presses = 0;
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 }).catch(() => undefined);
  while (Date.now() < giveUpAt && !(await live.isVisible().catch(() => false))) {
    if (presses < 3 && await press.isEnabled({ timeout: 500 }).catch(() => false)) {
      await press.click({ timeout: 5_000 }).catch(() => undefined);
      presses += 1;
      await page.waitForTimeout(3_000);
    }
    await page.waitForTimeout(500);
  }
  const resumed = await live.isVisible().catch(() => false);
  metrics.reload = {
    firedAt: at, resumed, presses, autoStarted: resumed && presses === 0, resumeMs: resumed ? Date.now() - at : null,
    providerAfter: await page.getByTestId('speaking-mic-indicator').getAttribute('data-live-provider', { timeout: 2_000 }).catch(() => null),
  };
  log('reload:', JSON.stringify(metrics.reload));
}
async function scheduleFault(label) {
  if (!fault.kind || faultScheduled) return;
  faultScheduled = true;
  const micStartedAt = (await page.evaluate(() => window.__micStartedAt).catch(() => null)) ?? Date.now();
  metrics.fault.tapeStartedAt = micStartedAt;
  const wait = Math.max(0, micStartedAt + fault.atSeconds * 1000 - Date.now());
  log(`fault ${fault.kind}: firing in ${(wait / 1000).toFixed(1)} s (${fault.atSeconds} s after the microphone tape started)`);
  // A failure here is recorded, never thrown: the run's finally block awaits this promise before it writes its artifacts.
  faultTimer = setTimeout(() => {
    faultFired = fireFault(label).catch((error) => {
      metrics.fault.error = String(error?.message ?? error).slice(0, 300);
      log('FAULT FAILED:', metrics.fault.error);
    });
  }, wait);
}

// A pinned run (VOICE_PROVIDER) is honoured only for a QA learner with the speaking_live_voice_pin:<learner id> feature flag.
// For anyone else the server answers pinned:false and the app carries on with its normal order, so a run that merely landed on
// the primary provider would look pinned. Fail as soon as the server's first answer to the provider= preflight says so.
async function assertPinHonoured() {
  if (!VOICE_PROVIDER) return;
  for (let i = 0; i < 80 && !preflights.some((p) => p.status === 200); i += 1) await page.waitForTimeout(250);
  if (preflights.some((p) => p.status === 200 && p.pinned !== true)) throw new Error(pinIgnoredMessage(VOICE_PROVIDER, learnerIdFromBearer(bearer)));
}

async function startLive(label) {
  liveCard = label;
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 });
  await assertPinHonoured();
  const start = page.getByRole('button', { name: 'Start speaking' });
  // Auto-start may already be connecting (button shown but disabled); click only when needed.
  if (await start.isEnabled({ timeout: 2_000 }).catch(() => false)) await start.click({ timeout: 5_000 }).catch(() => undefined);
  const t0 = Date.now();
  // Fail fast, with the reason, when no provider can start the session (production 29 Sep: OpenAI 429). The app shows
  // this only after every candidate failed: "The live AI patient could not start" (older builds showed the server's
  // "The realtime voice provider could not start this conversation"). Deliberately narrow: the microphone ("Could not
  // start the microphone") and exam-page ("Could not start the discussion") errors are other failures, and reporting
  // them as "no provider could start" would point the reader at the wrong place.
  const connected = page.getByText(LIVE_TEXT).first().waitFor({ timeout: 45_000 }).then(() => 'live');
  const refused = page.getByText(/(?:live AI patient|voice provider) could not start/i).first().waitFor({ timeout: 45_000 }).then(() => 'refused');
  const outcome = await Promise.race([connected, refused]).catch(() => 'timeout');
  connected.catch(() => undefined);
  refused.catch(() => undefined);
  if (outcome === 'refused') throw new Error(`${label}: no live voice provider could start the conversation (see metrics.providerAttempts and metrics.errors.http).`);
  if (outcome !== 'live') throw new Error(`${label}: live voice did not connect within 45 s.`);
  metrics[`${label}ConnectMs`] = Date.now() - t0;
  // When this card went live: where its window starts when the saved transcripts are compared with what the provider sent.
  metrics[`${label}LiveAt`] = Date.now();
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
// request was missed, one reload asks again. Returns what the page showed with the Transcript tab open (read before any
// reload, which would bring back the first tab): the wording check judges it apart from the rest of the page.
async function openTranscript(id) {
  await page.getByText('Transcript', { exact: true }).first().click({ timeout: 10_000 }).catch(() => undefined);
  await page.waitForTimeout(2_000);
  const tabText = await page.locator('body').innerText().catch(() => '');
  if (!findSegments(savedTranscripts[id]).length) {
    await nav(() => page.reload());
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => undefined);
    await page.waitForTimeout(3_000);
  }
  return tabText;
}

// ---- Reads and checks that go through the page itself ---------------------------------------------------------------------
// The results pages of this run as a learner read them, for checks.resultsWordingHonest: one entry per card or role-play
// ({ id, banner, overview, transcriptTab }) and the exam results page text.
const wordingPages = [];
let examResultsText = null;

// Calls the learner API from inside the page: same origin, its cookies, the bearer and device id it last sent. Retries when a
// navigation destroyed the page context mid-call. Resolves { status, body } (status 0 + error when the request itself failed).
async function apiFetch(path, method = 'GET', timeoutMs = 120_000) {
  for (let attempt = 1; ; attempt += 1) {
    try {
      return await page.evaluate(async (call) => {
        const csrf = document.cookie.match(/(?:^|; ?)oet_csrf=([^;]+)/)?.[1];
        const headers = { 'content-type': 'application/json' };
        if (call.bearer) headers.authorization = call.bearer;
        if (call.deviceId) headers['x-oet-device-id'] = call.deviceId;
        if (csrf) headers['x-csrf-token'] = csrf;
        try {
          const res = await fetch(`/api/backend${call.path}`, { method: call.method, credentials: 'include', headers, signal: AbortSignal.timeout(call.timeoutMs) });
          return { status: res.status, body: await res.json().catch(() => null) };
        } catch (error) {
          return { status: 0, body: null, error: String(error).slice(0, 200) };
        }
      }, { path, method, bearer, deviceId, timeoutMs });
    } catch (error) {
      if (attempt >= 3) throw error;
      await page.waitForTimeout(1_000);
    }
  }
}

// VERIFY_CREDITS: the QA learner's AI credits (GET /v1/me/ai-package-credits, the package ledger; /v1/me/ai/credits is the old
// token ledger). Kept per reading name; the verdict is judged at the end.
const creditSnaps = {};
async function readCredits(name) {
  for (let i = 0; i < 20 && !bearer; i += 1) await page.waitForTimeout(500); // the page's own first API call carries it
  const res = await apiFetch('/v1/me/ai-package-credits?pageSize=200');
  if (res.status !== 200 || !res.body) throw new Error(`The credit balance ("${name}") could not be read: HTTP ${res.status}${res.error ? ` ${res.error}` : ''}.`);
  creditSnaps[name] = res.body;
  log(`credits ${name}: ${creditPools(res.body)} (shared ${res.body.sharedCredits}, flexible ${res.body.flexibleCredits}, speaking ${res.body.speakingOnlyCredits})`);
  return res.body;
}
// A reading after the run began never ends it: a failed one is recorded, and the verdict then says the balance was not proved.
const tryReadCredits = (name) => readCredits(name).catch((error) => {
  metrics.credits.errors.push(String(error?.message ?? error));
  log('credits:', String(error?.message ?? error));
  return null;
});

// GRADE_RETRY: ask for the grade once more per card, after it was graded. The server answers with the existing assessment (200)
// or says it is still being graded (202); a new assessment, another status or a ledger change is a bug. At most two such calls
// a minute are allowed, which is why each is awaited.
async function retryGrading(ids) {
  metrics.aiAssessCallsBeforeRetry = { ...aiAssessCalls };
  metrics.gradeRetry = [];
  for (const sessionId of ids) {
    const assessmentOf = async () => (await apiFetch(`/v1/speaking/sessions/${sessionId}/ai-assessment`)).body?.assessmentId ?? null;
    const assessmentIdBefore = await assessmentOf();
    const t0 = Date.now();
    const again = await apiFetch(`/v1/speaking/sessions/${sessionId}/ai-assess`, 'POST');
    const assessmentIdAfter = await assessmentOf();
    metrics.gradeRetry.push({ sessionId, status: again.status, assessmentIdBefore, assessmentIdAfter, ms: Date.now() - t0 });
    log(`grade retry ${sessionId}: HTTP ${again.status} in ${Date.now() - t0} ms, assessment ${assessmentIdBefore} -> ${assessmentIdAfter}`);
  }
}

// VERIFY_CREDITS: the learner's History as the page shows it (GET /v1/me/attempts and /v1/submissions, captured while the page
// loads them) judged by historyVerdict: one row for the exam or the practice card, its credits and score, none in Past evidence.
async function verifyHistory(kind) {
  const answered = (route) => page.waitForResponse((r) => r.request().method() === 'GET' && route.test(r.url()), { timeout: 30_000 }).catch(() => null);
  const attemptsAnswer = answered(/\/v1\/me\/attempts(?:[?#]|$)/);
  const submissionsAnswer = answered(/\/v1\/submissions(?:[?#]|$)/);
  await nav(() => page.goto(`${APP}/submissions`));
  const [attemptsRes, submissionsRes] = await Promise.all([attemptsAnswer, submissionsAnswer]);
  const bodyOf = (res) => (res ? res.json().catch(() => null) : Promise.resolve(null));
  const attempts = await bodyOf(attemptsRes);
  const submissions = await bodyOf(submissionsRes);
  await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => undefined);
  await page.waitForTimeout(1_500);
  const text = await page.locator('body').innerText().catch(() => '');
  await shot('7-history').catch(() => undefined);
  const examRun = kind === 'exam';
  const runStart = Date.parse(metrics.startedAt);
  const speaking = (row) => String(row?.subtest ?? '').toLowerCase() === 'speaking';
  metrics.history = {
    attemptsCaptured: Boolean(attempts),
    submissionsCaptured: Boolean(submissions),
    speakingRows: (attempts?.items ?? []).filter((row) => speaking(row) && Date.parse(row.startedAt) >= runStart - 60_000),
    text: text.slice(0, 800),
    verdict: historyVerdict({
      kind,
      examId: metrics.examId,
      sessionId: metrics.sessionId,
      attempts: attempts?.items ?? [],
      submissions: submissions ? (submissions.items ?? []) : null,
      runStartMs: runStart,
      expectedCredits: examRun ? 4 : 2,
      expectedScore: examRun ? (examResults?.combinedScaledScore ?? null) : (aiAssessments[metrics.sessionId]?.estimatedScaledScore ?? null),
      pageText: text,
    }),
  };
  log('history:', JSON.stringify(metrics.history.verdict));
}

try {
  await page.goto(`${APP}/sign-in`);
  await page.locator('input[name="email"]').fill(QA_EMAIL);
  await page.locator('input[name="password"]').fill(QA_PASSWORD);
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL((u) => !u.pathname.startsWith('/sign-in'), { timeout: 60_000 });
  log('signed in');

  // VERIFY_CREDITS: read the balance now, before anything is spent, and refuse to start when it could not prove anything
  // (funded another way, or too few credits): the run then ends here with nothing billed.
  if (verifyCredits) {
    const refusal = creditPreflight(await readCredits('before'), { activities: MODE === 'exam' ? 2 : 1, exam: MODE === 'exam' });
    if (refusal.length) {
      metrics.credits.refused = refusal;
      throw new Error(`VERIFY_CREDITS: not starting, nothing was spent: ${refusal.join('; ')}.`);
    }
  }

  if (MODE === 'exam') {
    await nav(() => page.goto(`${APP}/speaking/exam`));
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
      const startDiscussion = page.getByRole('button', { name: /start the discussion now/i });
      await startDiscussion.waitFor({ timeout: 120_000 });
      // The card's 2-credit hold exists once its preparation screen is up (A when the introduction ends, B when A ends).
      if (verifyCredits) await tryReadCredits(card === 'A' ? 'afterCardAHold' : 'afterCardBHold');
      await startDiscussion.click({ timeout: 10_000 });
      await startLive(`card${card}`);
      cardText[card] = await page.locator('body').innerText().catch(() => '');
      await shot(`2-card-${card}-live`);
      // Each card ends automatically at 5:00; Card B's prep (or the results) follows.
      if (card === 'A') await page.getByRole('button', { name: /start the discussion now/i }).waitFor({ timeout: 7 * 60_000 });
      else await page.waitForURL(/\/speaking\/exam\/[^/]+\/results/, { timeout: 7 * 60_000 });
      await faultFired; // a refresh still reconnecting must settle before the card is judged
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
    examResultsText = text;
    if (verifyCredits) await tryReadCredits('afterGrade');
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
    const cardIds = examCards[1] && examCards[2] ? [examCards[1], examCards[2]] : [...sessionIds];
    for (const id of cardIds) {
      await nav(() => page.goto(`${APP}/speaking/sessions/${id}/results`));
      const cardResult = await waitForGrade(`card ${id}`, 5);
      const tabText = await openTranscript(id);
      wordingPages.push({ id, banner: false, overview: cardResult, transcriptTab: tabText });
      const body = await page.locator('body').innerText();
      await shot(`6-card-${id}-transcript`);
      metrics.cards.push({ sessionId: id, score: cardResult.match(/\d{3}\s*\/\s*500/)?.[0] ?? null, transcriptChars: body.length, transcriptShown: /doctor smith|how can i help|think about/i.test(body) });
    }
    metrics.examDto = examDto;
    await nav(() => page.goto(resultsUrl));
    if (gradeRetry) await retryGrading(cardIds);
    if (verifyCredits) {
      await tryReadCredits('final');
      await verifyHistory('exam');
    }
  } else {
    await nav(() => page.goto(`${APP}/speaking/roleplay/${CARD_ID}`));
    const consent = page.getByTestId('speaking-rules-consent');
    await consent.waitFor({ timeout: 60_000 });
    await shot('1-rules-consent');
    await consent.locator('input[type="checkbox"]').check();
    await consent.getByRole('button').last().click();
    await page.waitForURL(/\/speaking\/sessions\/[^/]+\/prep/, { timeout: 60_000 });
    await shot('2-prep');
    log('prep', page.url());
    // A free-sample card holds no credits, so its charge cannot be proved: refuse before a provider session is opened.
    if (verifyCredits) {
      const prepId = page.url().match(/sessions\/([^/?]+)/)?.[1];
      for (let i = 0; i < 20 && sessionDetails[prepId] === undefined; i += 1) await page.waitForTimeout(500);
      if (sessionDetails[prepId]?.isFreeSample) {
        metrics.credits.refused = ['this practice card is a free sample, which holds no credits'];
        throw new Error('VERIFY_CREDITS: not starting, no provider session was opened: this practice card is a free sample (it holds no credits), so the charge cannot be proved. Use another card_id, or a QA learner without an unused free sample.');
      }
    }
    await page.getByRole('button', { name: 'Start speaking now' }).click();
    await page.waitForURL(/\/speaking\/sessions\/[^/?]+(\?|$)/, { timeout: 60_000 });
    metrics.sessionId = page.url().match(/sessions\/([^/?]+)/)?.[1];
    // The card's 2-credit hold is taken when it is revealed, so it exists by now.
    if (verifyCredits) await tryReadCredits('afterHold');
    await startLive('roleplay');
    cardText.practice = await page.locator('body').innerText().catch(() => '');
    await shot('3-active-live');
    await page.waitForTimeout(speakSeconds * 1000);
    await faultFired; // a refresh still reconnecting must settle before the conversation is read
    await shot('4-active-after-conversation');
    const before = await readLive();
    metrics.micStartedAt = before.micStartedAt;
    // How much of the tape had played (seconds): the scripted lines a saved transcript should hold are those played to the end.
    metrics.tapeElapsedS = before.micStartedAt ? (Date.now() - before.micStartedAt) / 1000 : null;
    await snapshot();
    liveCard = null;
    log('provider calls:', providerCalls.join(', ') || '(none)', '| data-channel events:', before.events.length);
    if (VOICE_PROVIDER && !providerCalls.includes(VOICE_PROVIDER === 'openai' ? 'openai/offer' : 'gemini/token')) {
      throw new Error(preflights.some((p) => p.status === 200 && p.pinned !== true)
        ? pinIgnoredMessage(VOICE_PROVIDER, learnerIdFromBearer(bearer))
        : `Expected the ${VOICE_PROVIDER} provider, saw: ${providerCalls.join(', ') || 'none'}`);
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
      p95Ms: nearestRank(sorted, 0.95),
      perLine: lat,
    };
    // A refresh restarts the fake microphone tape in the new document, so its timings are not comparable with other runs.
    if (fault.kind === 'reload') metrics.latency.note = 'the tape restarted after the reload: not comparable';
    metrics.providerUsage = openAiServed()
      ? await openAiUsage()
      : { usageMetadataFrames: gemini.usage.length, last: gemini.usage.at(-1) ?? null, all: gemini.usage };
    log('METRICS', JSON.stringify({ ...metrics, providerUsage: { ...metrics.providerUsage, all: undefined } }));
    const text = await waitForGrade('role-play', 12);
    wordingPages.push({ id: metrics.sessionId, banner: true, overview: text, transcriptTab: '' });
    if (verifyCredits) await tryReadCredits('afterGrade');
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 2000));
    wordingPages[0].transcriptTab = await openTranscript(metrics.sessionId);
    await shot('6-transcript');
    if (gradeRetry) await retryGrading([metrics.sessionId]);
    if (verifyCredits) {
      await tryReadCredits('final');
      await verifyHistory('practice');
    }
  }
} catch (error) {
  failed = error;
  // The sign-in form still shows the typed QA e-mail, and the artifact outlives the run: no screenshot of it.
  if (!/\/sign-in/.test(page.url())) await shot('failure').catch(() => undefined);
} finally {
  // A fault that has not fired by now never will; one that is firing right now settles first.
  clearTimeout(faultTimer);
  await faultFired;
  await snapshot().catch(() => undefined);
  clearInterval(snapshotTimer);
  // A run that failed after spending still reports what it was charged.
  if (verifyCredits && creditSnaps.before && !creditSnaps.final) await tryReadCredits('final');
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
      // provider = who the server says saved it ("realtime-openai" / "realtime-gemini"): it must be the one the panel showed.
      const body = savedTranscripts[id];
      metrics.savedTranscripts[id] = {
        segments: segments.length, splitHazards: segments.length ? splitHazards(segments) : null,
        provider: body?.transcript?.provider ?? body?.status?.provider ?? null,
      };
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

  // ---- What the saved transcripts, credits, History and results pages say about this run ---------------------------------
  // Every judgement is a pure helper (live-voice-transcript-quality.mjs, live-voice-served-provider.mjs); a failure here is
  // recorded in the metrics, never thrown, so the run's other artifacts are never lost.
  metrics.preflights = preflights;
  metrics.aiAssessCalls = aiAssessCalls;
  metrics.softChecks = { gradedByClaude: null };
  const cardList = MODE === 'exam'
    ? (examCards[1] && examCards[2]
      ? [{ id: examCards[1], label: 'cardA', liveAt: metrics.cardALiveAt }, { id: examCards[2], label: 'cardB', liveAt: metrics.cardBLiveAt }]
      : [])
    : (metrics.sessionId ? [{ id: metrics.sessionId, label: 'roleplay', liveAt: metrics.roleplayLiveAt }] : []);
  metrics.sessionInputKinds = Object.fromEntries(cardList.map((card) => [card.id, sessionResults[card.id]?.inputKind ?? null]));
  // Each saved transcript as the grader reads it: well formed and complete against the tape (Q1-Q11), and equal to what the
  // provider sent (Q12) with the right labels and nothing from the other card. A card's window runs from 2 s before it went
  // live to 2 s before the next one did.
  const analysis = {};
  try {
    const wire = [
      ...(openAiServed() ? openAiWords(events).map((w) => ({ ...w, provider: 'openai' })) : []),
      ...(geminiServed() ? gemini.words.map((w) => ({ ...w, provider: 'gemini' })) : []),
    ].sort((a, b) => a.at - b.at);
    const script = scriptLines(scriptText);
    const tape = tokens(script.join(' '));
    const firedAt = faultMetrics.firedAt;
    // Where the run itself hid the conversation from the app (a stalled or dropped link): epoch ms, and on the tape's clock.
    const hidden = firedAt && (fault.kind === 'drop' || fault.kind === 'stall') ? [[firedAt, (faultMetrics.recoveredAt ?? firedAt) + 3_000]] : [];
    const hiddenS = faultMetrics.tapeStartedAt ? hidden.map((range) => range.map((t) => (t - faultMetrics.tapeStartedAt) / 1000)) : [];
    const playedUntilS = MODE === 'exam' ? 290 : (metrics.tapeElapsedS ?? Infinity);
    cardList.forEach((card, index) => {
      const segments = findSegments(savedTranscripts[card.id]);
      if (!segments.length) return;
      const faulted = index === 0 && Boolean(firedAt); // a fault hits the first live conversation only
      const restarted = faulted && fault.kind === 'reload'; // the tape began again in the new document
      const lines = expectedLines({ script, timeline, playedUntilS, excludeS: faulted ? hiddenS : [] });
      const closing = script.at(-1);
      const from = card.liveAt ? card.liveAt - 2_000 : null;
      const to = cardList[index + 1]?.liveAt ? cardList[index + 1].liveAt - 2_000 : Infinity;
      const quality = transcriptQuality({ segments, lines, closing: lines.at(-1)?.text === closing ? closing : null, tapeAligned: !restarted });
      const wireVerdict = from === null
        ? null
        : transcriptVerdict({ segments, wire, window: { from, to }, exclude: faulted ? hidden : [], tape: tape.length ? tape : null });
      if (restarted && metrics.reload) {
        // The words said before the refresh must still be in the saved transcript (the hook keeps them across the reload).
        const recall = (who) => speakerMatch({ segments, wire, who, from: from ?? -Infinity, to: firedAt, exclude: [] }).recall;
        metrics.reload.preReloadRecall = { candidate: recall('candidate'), patient: recall('patient') };
      }
      metrics.savedTranscripts[card.id].quality = quality;
      metrics.savedTranscripts[card.id].wire = wireVerdict;
      analysis[card.id] = { quality, wire: wireVerdict, segments };
    });
    if (cardList.length === 2 && analysis[cardList[0].id] && analysis[cardList[1].id]) {
      metrics.repeatedPatientText = repeatedPatientSegments(analysis[cardList[0].id].segments, analysis[cardList[1].id].segments);
    }
  } catch (error) {
    metrics.transcriptJudgeError = String(error?.stack ?? error).slice(0, 800);
    log('transcript judge error', metrics.transcriptJudgeError);
  }
  // Credits, grade retry, results wording, grader: judged from what the run read.
  const creditPrefix = MODE === 'exam' ? `exam:${metrics.examId}:` : `practice:${metrics.sessionId}`;
  try {
    const runId = MODE === 'exam' ? metrics.examId : metrics.sessionId;
    if (verifyCredits && !metrics.credits.refused && creditSnaps.before && runId) {
      const examRun = MODE === 'exam';
      metrics.credits.pools = Object.fromEntries(Object.entries(creditSnaps).map(([name, snapshot]) => [name, creditPools(snapshot)]));
      metrics.credits.verdict = creditVerdict({
        before: creditSnaps.before,
        after: creditSnaps.final,
        prefix: creditPrefix,
        expectedRefs: examRun ? [`${creditPrefix}cardA`, `${creditPrefix}cardB`] : [creditPrefix],
        steps: (examRun ? [['afterCardAHold', -2], ['afterCardBHold', -4], ['afterGrade', -4]] : [['afterHold', -2], ['afterGrade', -2]])
          .map(([name, delta]) => ({ name, snapshot: creditSnaps[name], delta })),
      });
    }
    if (gradeRetry && metrics.gradeRetry) {
      const ledgerOf = (snapshot) => (snapshot
        ? (snapshot.transactions ?? []).filter((t) => String(t.referenceId ?? '').startsWith(creditPrefix)).map((t) => `${t.referenceId}|${t.reason}|${creditRowDelta(t)}`)
        : null);
      metrics.gradeRetryVerdict = gradeRetryVerdict({ retries: metrics.gradeRetry, ledgerBefore: ledgerOf(creditSnaps.afterGrade), ledgerAfter: ledgerOf(creditSnaps.final) });
    }
    if (reported.length && (wordingPages.length || examResultsText !== null)) {
      metrics.resultsWording = resultsWordingVerdict({ sessions: wordingPages, exam: examResultsText });
    }
    // Who graded, from the page's own answers: informational (a fallback grader is not a failure of the live voice).
    const graded = MODE === 'exam' ? (examResults?.cards ?? []).map((card) => card.assessment).filter(Boolean) : [aiAssessments[metrics.sessionId]].filter(Boolean);
    metrics.grading = graded.map((a) => ({ provider: a.provider ?? null, modelId: a.modelId ?? null }));
    metrics.softChecks.gradedByClaude = metrics.grading.length ? metrics.grading.every((g) => /claude|opus|sonnet/i.test(`${g.provider} ${g.modelId}`)) : null;
  } catch (error) {
    metrics.judgeError = String(error?.stack ?? error).slice(0, 800);
    log('judge error', metrics.judgeError);
  }
  const verdicts = Object.values(analysis);
  // Several verdicts (one per card) become one check: false if any is false, null when none could be judged.
  const decided = (values) => {
    const known = values.filter((v) => typeof v === 'boolean');
    return known.length ? known.every(Boolean) : null;
  };
  const preReload = metrics.reload?.preReloadRecall;
  // Provider shown by each card's panel vs. the provider the server recorded on its saved transcript.
  const providerRows = cardList.map((card) => [metrics.savedTranscripts[card.id]?.provider ?? null, panelSeen[card.label]?.provider ?? null]);
  metrics.checks = {
    // The first provider the app tried is the one this run expected (an unpinned run with EXPECTED_PRIMARY set). Null
    // when nothing was expected, and when VOICE_PROVIDER pins one: then EXPECTED_PRIMARY is ignored
    // (metrics.expectedPrimaryIgnored), because a pinned run makes one attempt and never "tries first".
    primaryProviderIsExpected: !VOICE_PROVIDER && EXPECTED_PRIMARY ? providerCalls[0] === primaryCall : null,
    // A pinned run is served by the provider it pinned, in exam mode too (the page could lose ?voiceProvider=).
    pinnedProviderServed: VOICE_PROVIDER && reported.length ? reported.every((p) => p === VOICE_PROVIDER) : null,
    // The QA pin: with VOICE_PROVIDER every answered provider= preflight says pinned:true for that provider (the server honours
    // it only for a flagged QA learner). Null without VOICE_PROVIDER.
    pinHonoured: pinHonoured(preflights, VOICE_PROVIDER),
    // FAIL_PRIMARY: the create calls are exactly [failed primary, secondary] per card, plus what a recovery or a reload adds
    // on the first card (failoverCallsOk), and the panel reports the other provider as serving after a failover.
    failoverAsRequested: failPrimary
      ? failoverObserved
        && failoverCallsOk({
          calls: providerCalls, primaryCall, secondaryCall, cards: panels.length,
          recoveries: fault.kind === 'drop' || fault.kind === 'stall' ? (faultedPanel?.recoveries ?? 0) : 0,
          reloads: fault.kind === 'reload' && faultMetrics.firedAt ? 1 : 0,
        })
        && panels.every((p) => p?.provider === secondaryName && p.failedOver === true)
      : null,
    // FAIL_PRIMARY together with a fault: the fault hit the fallback provider (the one that was serving).
    faultHitFallback: failPrimary && fault.kind ? faultMetrics.provider === secondaryName : null,
    // FAULT_DROP_AT_S / FAULT_STALL_AT_S: the fault fired, the faulted card's panel reports >= 1 recovery, the patient spoke
    // again after the recovery session was asked for, and the panel did not end in the error state. Null without a fault
    // (and for a reload, which has no recovery: reloadResumed and reloadKeepsTranscript judge it).
    recoveredAsRequested: recoveredAsRequested({
      fault: faultMetrics, recoveries: faultedPanel?.recoveries ?? null, errorShown: faultedPanel?.error ?? null, patientAt,
    }),
    // FAULT_RELOAD_AT_S: the refresh was applied, the patient was back (by itself or after Start speaking), no error showed; and
    // what was said before the refresh (both speakers, >= 80% of the provider's words) is still in the saved transcript.
    reloadResumed: fault.kind === 'reload' ? Boolean(metrics.reload?.resumed) && faultedPanel?.error !== true : null,
    reloadKeepsTranscript: fault.kind === 'reload'
      ? Boolean(preReload) && decided([preReload.candidate, preReload.patient].map((recall) => (typeof recall === 'number' ? recall >= 0.8 : null))) === true
      : null,
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
    // Saved transcripts, judged as the grader reads them (see analysis above). transcriptQuality = Q1-Q11 on every card;
    // transcriptsMatchWire = each card's saved words equal what the provider sent in its window (Q12); candidateLabelsAreTheTape =
    // the candidate label carries the scripted tape and the patient label does not; noCrossCardLeak = every saved patient
    // segment is the patient's own words in this card's window and the two cards share at most one long patient sentence.
    transcriptQuality: verdicts.length ? verdicts.every((v) => v.quality.ok) : null,
    transcriptsMatchWire: decided(verdicts.map((v) => v.wire?.matchesWire)),
    candidateLabelsAreTheTape: decided(verdicts.map((v) => v.wire?.labelsAreTape)),
    // One stock patient sentence (the TEACH-BACK "you haven't told me what it is yet") may legitimately come back in both cards; a card saved twice repeats many.
    noCrossCardLeak: decided([...verdicts.map((v) => v.wire?.patientContained), metrics.repeatedPatientText ? metrics.repeatedPatientText.length < 2 : undefined]),
    // The server recorded the transcript under the provider the panel showed while the card was live.
    savedProviderMatchesServed: reported.length && providerRows.length ? providerRows.every(([saved, shown]) => Boolean(saved) && saved === `realtime-${shown}`) : null,
    // GET .../results says what each session handed in: a live conversation's transcript, never a recording (live runs only).
    inputKindLiveVoice: reported.length && cardList.length ? cardList.every((card) => sessionResults[card.id]?.inputKind === 'live_voice') : null,
    // VERIFY_CREDITS: charged exactly once (4 for an exam, 2 for practice), each hold where it belongs, nothing refunded; null
    // when the run was refused before it started or did not get far enough to be judged.
    creditsDeductedOnce: metrics.credits?.verdict ? metrics.credits.verdict.ok : null,
    // GRADE_RETRY: asking for the grade again changed nothing (200/202, the same assessment, the same ledger).
    gradeRetryIdempotent: metrics.gradeRetryVerdict ? metrics.gradeRetryVerdict.ok : null,
    // VERIFY_CREDITS: the History page lists the run once, with its route, credits and score (see historyVerdict).
    historyListsExam: metrics.history?.verdict ? metrics.history.verdict.ok : null,
    // Live runs: no results page says "recording" or shows an audio player, each says it was a live conversation, and the exam
    // results show a readable band and the advisory sentence (see resultsWordingVerdict).
    resultsWordingHonest: metrics.resultsWording ? metrics.resultsWording.ok : null,
    // The live screens (card text while live) never name a provider: learners are not told "OpenAI", "Gemini" or "GPT-Live".
    noProviderNamesInUi: reported.length && Object.values(cardText).some(Boolean)
      ? !Object.values(cardText).some((text) => /\b(?:openai|gemini|gpt-live)\b/i.test(text))
      : null,
  };
  log('CHECKS', JSON.stringify(metrics.checks), 'CONVERSATION', JSON.stringify(c));
  log('SOFT CHECKS (never fail the run)', JSON.stringify(metrics.softChecks));
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
