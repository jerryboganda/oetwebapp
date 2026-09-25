// Production browser E2E for the live AI patient (Gemini Live or OpenAI GPT-Live).
// Real Chromium at phone width, fake microphone playing a scripted candidate,
// real learner pages: rules + consent -> prep -> live role-play -> submit -> result.
// Run by .github/workflows/speaking-live-voice-prod-e2e.yml (never locally).
import { chromium, devices } from 'playwright';
import fs from 'node:fs';

const APP = process.env.APP_URL ?? 'https://app.oetwithdrhesham.co.uk';
const { QA_EMAIL, QA_PASSWORD, QA_DEVICE_ID = '', CARD_ID, CANDIDATE_WAV, SPEAK_SECONDS = '110', VOICE_PROVIDER = '' } = process.env;
const out = 'live-voice-e2e';
fs.mkdirSync(out, { recursive: true });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

const browser = await chromium.launch({
  args: [
    '--use-fake-ui-for-media-stream',
    '--use-fake-device-for-media-stream',
    `--use-file-for-fake-audio-capture=${CANDIDATE_WAV}`,
    '--autoplay-policy=no-user-gesture-required',
  ],
});
const context = await browser.newContext({ ...devices['Pixel 7'], permissions: ['microphone'] });
// ?voiceProvider=<p> on the session page selects the provider; add it to the
// client-side navigation into /speaking/sessions/{id}. GPT-Live talks over a
// WebRTC data channel, so mirror its events where the test can read them.
// A fresh CI browser is an unknown device (emailed-code verification); reuse
// the QA learner's already-approved device identity (lib/device-id.ts).
if (QA_DEVICE_ID) await context.addInitScript((id) => { try { localStorage.setItem('oet_device_id', id); } catch { /* cookie fallback */ } }, QA_DEVICE_ID);
await context.addInitScript((provider) => {
  window.__voiceEvents = [];
  const createDataChannel = RTCPeerConnection.prototype.createDataChannel;
  RTCPeerConnection.prototype.createDataChannel = function (...args) {
    const channel = createDataChannel.apply(this, args);
    channel.addEventListener('message', (e) => { try { window.__voiceEvents.push(JSON.parse(e.data)); } catch { /* non-JSON */ } });
    return channel;
  };
  if (!provider) return;
  for (const method of ['pushState', 'replaceState']) {
    const original = history[method].bind(history);
    history[method] = (state, title, url) => {
      const target = url == null ? url : String(url);
      return original(state, title, target && /\/speaking\/sessions\/[^/?]+$/.test(target) ? `${target}?voiceProvider=${provider}` : target);
    };
  }
}, VOICE_PROVIDER);
const page = await context.newPage();
const transcript = { candidate: '', patient: '' };
let providerError = null;
const providerCalls = [];
page.on('request', (r) => { const m = r.url().match(/\/realtime\/sessions\/[^/]+\/(openai\/offer|gemini\/token)/); if (m) providerCalls.push(m[1]); });
const readOpenAiEvents = async () => {
  const events = await page.evaluate(() => window.__voiceEvents ?? []).catch(() => []);
  const text = (type) => events.filter((e) => e.type === type).map((e) => e.delta ?? '').join('');
  if (events.length) {
    transcript.candidate = text('session.input_transcript.delta');
    transcript.patient = text('session.output_transcript.delta');
    const err = events.find((e) => String(e.type).includes('error'));
    if (err) providerError = JSON.stringify(err);
  }
  return events.length;
};
page.on('websocket', (ws) => {
  if (!ws.url().includes('generativelanguage.googleapis.com')) return;
  log('Gemini Live socket opened');
  ws.on('framereceived', ({ payload }) => {
    try {
      const v = JSON.parse(typeof payload === 'string' ? payload : Buffer.from(payload).toString('utf8'));
      if (v.error) providerError = JSON.stringify(v.error);
      const sc = v.serverContent;
      if (sc?.inputTranscription?.text) transcript.candidate += sc.inputTranscription.text;
      if (sc?.outputTranscription?.text) transcript.patient += sc.outputTranscription.text;
      if (sc?.turnComplete) log('patient turn complete; patient so far:', transcript.patient.slice(-240));
    } catch { /* audio or non-JSON frame */ }
  });
  ws.on('close', () => log('Gemini Live socket closed'));
});
page.on('console', (m) => { if (m.type() === 'error') log('console.error', m.text().slice(0, 200)); });

const shot = (name) => page.screenshot({ path: `${out}/${name}.png`, fullPage: true });
let failed = null;
try {
  await page.goto(`${APP}/sign-in`);
  await page.locator('input[name="email"]').fill(QA_EMAIL);
  await page.locator('input[name="password"]').fill(QA_PASSWORD);
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL((u) => !u.pathname.startsWith('/sign-in'), { timeout: 60_000 });
  log('signed in');

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
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 });
  const start = page.getByRole('button', { name: 'Start speaking' });
  if (await start.isVisible().catch(() => false)) await start.click();
  await page.getByText(/Live — the patient is listening|Patient speaking/).waitFor({ timeout: 45_000 });
  log('live voice connected');
  await shot('3-active-live');
  await page.waitForTimeout(Number(SPEAK_SECONDS) * 1000);
  await shot('4-active-after-conversation');
  log('provider calls:', providerCalls.join(', ') || '(none)', '| data-channel events:', await readOpenAiEvents());
  if (VOICE_PROVIDER && !providerCalls.includes(VOICE_PROVIDER === 'openai' ? 'openai/offer' : 'gemini/token')) {
    throw new Error(`Expected the ${VOICE_PROVIDER} provider, saw: ${providerCalls.join(', ') || 'none'}`);
  }
  if (providerError) throw new Error(`Provider reported: ${providerError}`);
  if (!transcript.patient.trim()) throw new Error('The AI patient never spoke.');
  await page.getByRole('button', { name: 'Finish & submit' }).click();
  await page.getByRole('button', { name: 'Submit now' }).click();
  await page.waitForURL(/\/results/, { timeout: 90_000 });
  log('submitted', page.url());
  const deadline = Date.now() + 12 * 60_000; // max-reasoning grading takes ~7-8 min
  let graded = false;
  while (Date.now() < deadline) {
    const text = await page.locator('body').innerText();
    if (/Try grading again/i.test(text)) throw new Error('Grading failed on the results page.');
    if (!/processing|being graded|analysing|Check again/i.test(text) && /\d{3}\s*\/\s*500|criteri/i.test(text)) { graded = true; break; }
    await page.waitForTimeout(10_000);
    await page.reload();
  }
  await shot('5-result');
  log('RESULT PAGE:\n' + (await page.locator('body').innerText()).slice(0, 2000));
  if (!graded) throw new Error('No graded result within 12 minutes.');
} catch (error) {
  failed = error;
  await shot('failure').catch(() => undefined);
} finally {
  fs.writeFileSync(`${out}/transcript.json`, JSON.stringify(transcript, null, 2));
  log('CANDIDATE (as heard by the provider):', transcript.candidate.trim().slice(0, 1500));
  log('PATIENT (AI):', transcript.patient.trim().slice(0, 3000));
  await browser.close();
}
if (failed) {
  console.error(failed);
  process.exit(1);
}
