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
  const getUserMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
  navigator.mediaDevices.getUserMedia = async (constraints) => {
    const stream = await getUserMedia(constraints);
    window.__micStartedAt ??= Date.now();
    return stream;
  };
  const chunks = [];
  const watchRemote = (stream) => {
    try {
      const recorder = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
      recorder.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
      recorder.start(1000);
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
page.on('console', (m) => { if (m.type() === 'error') log('console.error', m.text().replace(/access_token=[^'" ]+/g, 'access_token=REDACTED').slice(0, 600)); });

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
    metrics.providerUsage = VOICE_PROVIDER === 'openai'
      ? await openAiUsage()
      : { usageMetadataFrames: gemini.usage.length, all: gemini.usage };
    await saveOpenAiAudio('patient-audio-exam');
    log('exam submitted', page.url());
    const text = await waitForGrade('exam', 20);
    await shot('5-result');
    log('RESULT PAGE:\n' + text.slice(0, 3000));
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
  metrics.stability = stability;
  fs.writeFileSync(`${out}/transcript.json`, JSON.stringify(transcript, null, 2));
  fs.writeFileSync(`${out}/metrics.json`, JSON.stringify(metrics, null, 2));
  log('CANDIDATE (as heard by the provider):', transcript.candidate.trim().slice(0, 4000));
  log('PATIENT (AI):', transcript.patient.trim().slice(0, 6000));
  await browser.close();
}
if (failed) {
  console.error(failed);
  process.exit(1);
}
