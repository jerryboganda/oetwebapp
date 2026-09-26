// Production browser E2E for the live AI patient (Gemini Live or OpenAI GPT-Live).
// Real Chromium at phone width, fake microphone playing a scripted candidate,
// real learner pages. MODE=practice: rules + consent -> prep -> live role-play
// -> submit -> result. MODE=exam: intro consent -> Card A -> Card B -> results.
// Measures, identically for both providers: provider-reported usage, time from
// the end of each candidate line to the patient's first audio, stability events,
// and records the patient's audio for a listening check.
// Run by .github/workflows/speaking-live-voice-prod-e2e.yml (never locally).
import { chromium, devices } from 'playwright';
import fs from 'node:fs';

const APP = process.env.APP_URL ?? 'https://app.oetwithdrhesham.co.uk';
const {
  QA_EMAIL, QA_PASSWORD, QA_DEVICE_ID = '', CARD_ID = '', CANDIDATE_WAV, CANDIDATE_TIMELINE = '',
  SPEAK_SECONDS = '110', VOICE_PROVIDER = '', MODE = 'practice',
} = process.env;
const out = 'live-voice-e2e';
fs.mkdirSync(out, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const timeline = CANDIDATE_TIMELINE && fs.existsSync(CANDIDATE_TIMELINE) ? JSON.parse(fs.readFileSync(CANDIDATE_TIMELINE, 'utf8')) : [];

const browser = await chromium.launch({
  args: [
    '--use-fake-ui-for-media-stream',
    '--use-fake-device-for-media-stream',
    `--use-file-for-fake-audio-capture=${CANDIDATE_WAV}`,
    '--autoplay-policy=no-user-gesture-required',
  ],
});
const context = await browser.newContext({ ...devices['Pixel 7'], permissions: ['microphone'] });
// A fresh CI browser is an unknown device (emailed-code verification); reuse
// the QA learner's already-approved device identity (lib/device-id.ts).
if (QA_DEVICE_ID) await context.addInitScript((id) => { try { localStorage.setItem('oet_device_id', id); } catch { /* cookie fallback */ } }, QA_DEVICE_ID);
// ?voiceProvider=<p> on the session/exam page selects the provider; add it to the
// client-side navigation. GPT-Live talks over a WebRTC data channel and media
// track, so mirror its events, detect patient-audio onsets and record the audio.
await context.addInitScript((provider) => {
  window.__voiceEvents = [];
  window.__audioOnsets = [];
  window.__micStartedAt = null;
  window.__docId = Math.random().toString(36).slice(2);
  // Speech spans [start, end] per side, from the audio itself: the candidate's
  // mic and the patient's remote track (barge-in, talk-over, silence checks).
  window.__micSpans = [];
  window.__patientSpans = [];
  const spans = (stream, list) => {
    const ctx = new AudioContext();
    const analyser = ctx.createAnalyser();
    ctx.createMediaStreamSource(stream).connect(analyser);
    const buf = new Float32Array(analyser.fftSize);
    let open = false;
    let lastLoud = 0;
    setInterval(() => {
      analyser.getFloatTimeDomainData(buf);
      const now = Date.now();
      if (Math.sqrt(buf.reduce((s, v) => s + v * v, 0) / buf.length) > 0.01) {
        if (!open) list.push([now, now]);
        open = true;
        lastLoud = now;
        list[list.length - 1][1] = now;
      } else if (open && now - lastLoud > 400) open = false;
    }, 25);
  };
  const getUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
  navigator.mediaDevices.getUserMedia = async (constraints) => {
    const stream = await getUserMedia(constraints);
    window.__micStartedAt ??= Date.now();
    try { spans(stream, window.__micSpans); } catch { /* best effort */ }
    return stream;
  };
  const chunks = [];
  const watchRemote = (stream) => {
    try {
      const recorder = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
      recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
      recorder.start(1000);
      spans(stream, window.__patientSpans);
      const ctx = new AudioContext();
      const analyser = ctx.createAnalyser();
      ctx.createMediaStreamSource(stream).connect(analyser);
      const buf = new Float32Array(analyser.fftSize);
      let silentSince = Date.now();
      let speaking = false;
      setInterval(() => {
        analyser.getFloatTimeDomainData(buf);
        const rms = Math.sqrt(buf.reduce((s, v) => s + v * v, 0) / buf.length);
        if (rms > 0.01) {
          if (!speaking && Date.now() - silentSince > 500) window.__audioOnsets.push(Date.now());
          speaking = true;
        } else {
          if (speaking) silentSince = Date.now();
          speaking = false;
        }
      }, 25);
    } catch { /* recording is best effort */ }
  };
  window.__patientAudio = async () => {
    const bytes = new Uint8Array(await new Blob(chunks).arrayBuffer());
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(binary);
  };
  const Native = window.RTCPeerConnection;
  window.RTCPeerConnection = class extends Native {
    constructor(...args) {
      super(...args);
      this.addEventListener('track', (e) => watchRemote(e.streams[0] ?? new MediaStream([e.track])));
    }
  };
  const createDataChannel = Native.prototype.createDataChannel;
  Native.prototype.createDataChannel = function (...args) {
    const channel = createDataChannel.apply(this, args);
    channel.addEventListener('message', (e) => {
      try {
        const event = { ...JSON.parse(e.data), __at: Date.now() };
        window.__voiceEvents.push(event);
        // session.closed (final billed seconds) lands while the page navigates
        // to the results; keep usage events where the next document can read them.
        if (event.type === 'session.closed' || event.type === 'session.usage.updated') {
          const kept = JSON.parse(localStorage.getItem('__oai_usage') || '[]');
          kept.push({ type: event.type, seconds: event.usage?.seconds ?? null, reason: event.reason ?? null, at: event.__at });
          localStorage.setItem('__oai_usage', JSON.stringify(kept));
        }
      } catch { /* non-JSON */ }
    });
    return channel;
  };
  if (!provider) return;
  for (const method of ['pushState', 'replaceState']) {
    const original = history[method].bind(history);
    history[method] = (state, title, url) => {
      const target = url == null ? url : String(url);
      const live = target && /\/speaking\/(sessions|exam)\/[^/?]+$/.test(target);
      return original(state, title, live ? `${target}?voiceProvider=${provider}` : target);
    };
  }
}, VOICE_PROVIDER);

const page = await context.newPage();
const transcript = { candidate: '', patient: '' };
const stability = { providerErrors: [], socketsOpened: 0, socketCloses: [], sessionClosed: [] };
const gemini = { usage: [], audioChunks: [], audioAt: [] };
const providerCalls = [];
page.on('request', (r) => { const m = r.url().match(/\/realtime\/sessions\/[^/]+\/(openai\/offer|gemini\/token)/); if (m) providerCalls.push(m[1]); });
page.on('websocket', (ws) => {
  if (!ws.url().includes('generativelanguage.googleapis.com')) return;
  stability.socketsOpened += 1;
  log('Gemini Live socket opened');
  ws.on('framereceived', ({ payload }) => {
    const at = Date.now();
    try {
      const v = JSON.parse(typeof payload === 'string' ? payload : Buffer.from(payload).toString('utf8'));
      if (v.error) stability.providerErrors.push(JSON.stringify(v.error));
      if (v.usageMetadata) gemini.usage.push({ at, ...v.usageMetadata });
      const sc = v.serverContent;
      if (sc?.inputTranscription?.text) transcript.candidate += sc.inputTranscription.text;
      if (sc?.outputTranscription?.text) transcript.patient += sc.outputTranscription.text;
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
page.on('console', (m) => { if (m.type() === 'error') { errors.console.push(redact(m.text())); log('console.error', redact(m.text())); } });
page.on('pageerror', (e) => { errors.page.push(redact(String(e))); log('pageerror', redact(String(e))); });
page.on('requestfailed', (r) => {
  const reason = r.failure()?.errorText ?? '';
  // Navigations and polls abandoned by a page change report ERR_ABORTED; not a failure.
  if (!/ERR_ABORTED/.test(reason)) errors.requestFailed.push(`${r.method()} ${redact(r.url())} ${reason}`);
});
const sessionIds = new Set();
let examDto = null;
page.on('response', async (r) => {
  const url = r.url();
  for (const m of url.matchAll(/\/sessions\/(sps_[a-f0-9]+)/g)) sessionIds.add(m[1]);
  if (r.status() >= 400 && /oetwithdrhesham|googleapis|openai/.test(url)) errors.http.push(`${r.status()} ${r.request().method()} ${redact(url)}`);
  if (/\/v1\/speaking\/exams\/[^/?]+$/.test(url) && r.ok()) examDto = await r.json().catch(() => examDto);
});
// Snapshot the page's audio spans and data-channel events every few seconds,
// keyed by document, so nothing is lost when a card or the results page loads.
const snapshots = {};
const snapshot = async () => {
  const s = await page.evaluate(() => ({
    docId: window.__docId, events: window.__voiceEvents ?? [], mic: window.__micSpans ?? [], patient: window.__patientSpans ?? [],
  })).catch(() => null);
  if (s?.docId && (s.events.length || s.mic.length)) snapshots[s.docId] = s;
};
const snapshotTimer = setInterval(snapshot, 3_000);

const readOpenAi = async () => {
  const state = await page.evaluate(() => ({ events: window.__voiceEvents ?? [], onsets: window.__audioOnsets ?? [], micStartedAt: window.__micStartedAt }))
    .catch(() => ({ events: [], onsets: [], micStartedAt: null }));
  const text = (type) => state.events.filter((e) => e.type === type).map((e) => e.delta ?? '').join('');
  if (state.events.length) {
    transcript.candidate = text('session.input_transcript.delta');
    transcript.patient = text('session.output_transcript.delta');
    stability.providerErrors = state.events.filter((e) => String(e.type).includes('error')).map((e) => JSON.stringify(e));
    stability.sessionClosed = state.events.filter((e) => e.type === 'session.closed').map((e) => ({ reason: e.reason, usage: e.usage }));
  }
  return state;
};

// Patient onset = first patient audio after a >=500 ms gap. Latency for line i =
// first onset after the line ends (within 15 s).
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
function geminiOnsets(times) {
  return times.filter((t, i) => i === 0 || t - times[i - 1] > 500);
}
// Conversation behaviour from the two speech tracks (candidate mic, patient audio).
const merge = (list) => list.slice().sort((a, b) => a[0] - b[0]).reduce((acc, [s, e]) => {
  const last = acc.at(-1);
  if (last && s <= last[1] + 400) last[1] = Math.max(last[1], e);
  else acc.push([s, e]);
  return acc;
}, []);
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
    return c ? [{ at: p0, overlapMs: Math.min(p1, c[1]) - p0, patientMs: p1 - p0 }] : [];
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

function wav(pcm, rate = 24_000) {
  const header = Buffer.alloc(44);
  header.write('RIFF', 0); header.writeUInt32LE(36 + pcm.length, 4); header.write('WAVEfmt ', 8);
  header.writeUInt32LE(16, 16); header.writeUInt16LE(1, 20); header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24); header.writeUInt32LE(rate * 2, 28); header.writeUInt16LE(2, 32); header.writeUInt16LE(16, 34);
  header.write('data', 36); header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}

const shot = (name) => page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
const metrics = { mode: MODE, provider: VOICE_PROVIDER || 'primary', cardId: CARD_ID, speakSeconds: Number(SPEAK_SECONDS) };
let failed = null;

async function startLive(label) {
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 });
  const start = page.getByRole('button', { name: 'Start speaking' });
  // Auto-start may already be connecting (button shown but disabled); click only when needed.
  if (await start.isEnabled({ timeout: 2_000 }).catch(() => false)) await start.click({ timeout: 5_000 }).catch(() => undefined);
  const t0 = Date.now();
  await page.getByText(/Live — the patient is listening|Patient speaking/).waitFor({ timeout: 45_000 });
  metrics[`${label}ConnectMs`] = Date.now() - t0;
  log(`${label}: live voice connected`);
}

async function waitForGrade(label, minutes) {
  const deadline = Date.now() + minutes * 60_000;
  while (Date.now() < deadline) {
    // Let the page render (not its loading skeleton) before reading it.
    await page.waitForLoadState('networkidle', { timeout: 20_000 }).catch(() => undefined);
    const text = await page.locator('body').innerText();
    if (/Try grading again/i.test(text)) throw new Error(`${label}: grading failed on the results page.`);
    if (!/processing|being graded|analysing|Check again/i.test(text) && /\d{3}\s*\/\s*500|criteri/i.test(text)) return text;
    await page.waitForTimeout(30_000); // gentle: 10 s reloads tripped the API rate limit
    await page.reload();
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

async function saveOpenAiAudio(name) {
  const audio = await page.evaluate(() => window.__patientAudio?.()).catch(() => null);
  if (audio) fs.writeFileSync(`${out}/${name}.webm`, Buffer.from(audio, 'base64'));
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
      await shot(`2-card-${card}-live`);
      // Each card ends automatically at 5:00; Card B's prep (or the results) follows.
      if (card === 'A') await page.getByRole('button', { name: /start the discussion now/i }).waitFor({ timeout: 7 * 60_000 });
      else await page.waitForURL(/\/speaking\/exam\/[^/]+\/results/, { timeout: 7 * 60_000 });
      log(`card ${card} finished; provider calls so far: ${providerCalls.join(', ')}`);
      await shot(`3-after-card-${card}`);
    }
    metrics.providerCalls = providerCalls;
    metrics.providerUsage = providerCalls.includes('openai/offer')
      ? await openAiUsage()
      : { usageMetadataFrames: gemini.usage.length, all: gemini.usage };
    await saveOpenAiAudio('patient-audio-exam');
    log('exam submitted', page.url());
    const text = await waitForGrade('exam', 25);
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 3000));
    metrics.examResult = text.match(/\d{3}\s*\/\s*500[^\n]*/g) ?? [];
    // After completion: the combined result survives a reload, and each card's
    // own results page and transcript load.
    const resultsUrl = page.url();
    await page.reload();
    metrics.examResultAfterReload = (await waitForGrade('exam (reload)', 2)).match(/\d{3}\s*\/\s*500[^\n]*/g) ?? [];
    metrics.cards = [];
    for (const id of sessionIds) {
      await page.goto(`${APP}/speaking/sessions/${id}/results`);
      const cardText = await waitForGrade(`card ${id}`, 5);
      await page.getByText('Transcript', { exact: true }).first().click({ timeout: 10_000 }).catch(() => undefined);
      await page.waitForTimeout(2_000);
      const body = await page.locator('body').innerText();
      await shot(`6-card-${id}-transcript`);
      metrics.cards.push({ sessionId: id, score: cardText.match(/\d{3}\s*\/\s*500/)?.[0] ?? null, transcriptChars: body.length, transcriptShown: /doctor smith|how can i help|think about/i.test(body) });
    }
    metrics.examDto = examDto;
    await page.goto(resultsUrl);
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
    await shot('3-active-live');
    await page.waitForTimeout(Number(SPEAK_SECONDS) * 1000);
    await shot('4-active-after-conversation');
    const before = await readOpenAi();
    log('provider calls:', providerCalls.join(', ') || '(none)', '| data-channel events:', before.events.length);
    if (VOICE_PROVIDER && !providerCalls.includes(VOICE_PROVIDER === 'openai' ? 'openai/offer' : 'gemini/token')) {
      throw new Error(`Expected the ${VOICE_PROVIDER} provider, saw: ${providerCalls.join(', ') || 'none'}`);
    }
    if (stability.providerErrors.length) throw new Error(`Provider reported: ${stability.providerErrors[0]}`);
    if (!transcript.patient.trim()) throw new Error('The AI patient never spoke.');
    if (before.events.length) await saveOpenAiAudio('patient-audio');
    await page.getByRole('button', { name: 'Finish & submit' }).click();
    await page.getByRole('button', { name: 'Submit now' }).click();
    await page.waitForURL(/\/results/, { timeout: 90_000 });
    log('submitted', page.url());
    // The results page is a new document: the OpenAI events were read above.
    const onsets = before.events.length ? before.onsets : geminiOnsets(gemini.audioAt);
    const lat = latencies(before.micStartedAt, onsets);
    const sorted = lat.map((l) => l.ms).sort((a, b) => a - b);
    metrics.latency = {
      measuredTurns: lat.length,
      scriptedLines: timeline.length,
      medianMs: sorted[Math.floor(sorted.length / 2)] ?? null,
      p90Ms: sorted[Math.floor(sorted.length * 0.9)] ?? null,
      perLine: lat,
    };
    metrics.providerUsage = before.events.length
      ? await openAiUsage()
      : { usageMetadataFrames: gemini.usage.length, last: gemini.usage.at(-1) ?? null, all: gemini.usage };
    if (gemini.audioChunks.length) fs.writeFileSync(`${out}/patient-audio.wav`, wav(Buffer.concat(gemini.audioChunks)));
    log('METRICS', JSON.stringify({ ...metrics, providerUsage: { ...metrics.providerUsage, all: undefined } }));
    const text = await waitForGrade('role-play', 12);
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 2000));
  }
} catch (error) {
  failed = error;
  await shot('failure').catch(() => undefined);
} finally {
  clearInterval(snapshotTimer);
  const docs = Object.values(snapshots);
  const events = docs.flatMap((d) => d.events);
  if (events.length) {
    transcript.candidate = events.filter((e) => e.type === 'session.input_transcript.delta').map((e) => e.delta ?? '').join('');
    transcript.patient = events.filter((e) => e.type === 'session.output_transcript.delta').map((e) => e.delta ?? '').join('');
    stability.providerErrors = events.filter((e) => String(e.type).includes('error')).map((e) => JSON.stringify(e));
    stability.sessionClosed = events.filter((e) => e.type === 'session.closed').map((e) => ({ reason: e.reason, usage: e.usage }));
  }
  metrics.conversation = conversation(docs.flatMap((d) => d.mic), docs.flatMap((d) => d.patient));
  metrics.outOfRole = outOfRole(transcript.patient);
  metrics.errors = errors;
  metrics.providerCalls = providerCalls;
  const c = metrics.conversation;
  metrics.checks = {
    liveProviderIsDefaultOpenAi: !VOICE_PROVIDER ? providerCalls.length > 0 && providerCalls.every((p) => p === 'openai/offer') : null,
    bargeInStopsWithin1500ms: c.bargeIns.length ? c.bargeIns.every((b) => b.patientStoppedAfterMs <= 1_500) : null,
    noPatientTalkOver: c.talkOver.every((t) => t.overlapMs < 1_000),
    survivesSilences: c.silences.length ? c.silences.every((s) => s.repliedToNextLineMs !== null) && !stability.providerErrors.length : null,
    staysInRole: metrics.outOfRole.length === 0,
    noBrowserErrors: !errors.console.length && !errors.page.length && !errors.http.length && !errors.requestFailed.length,
  };
  log('CHECKS', JSON.stringify(metrics.checks), 'CONVERSATION', JSON.stringify(c));
  metrics.stability = stability;
  fs.writeFileSync(`${out}/transcript.json`, JSON.stringify(transcript, null, 2));
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
