// Production browser E2E for the live AI patient (Gemini Live).
// Real Chromium at phone width, fake microphone playing a scripted candidate,
// real learner pages: rules + consent -> prep -> live role-play -> submit -> result.
// Run by .github/workflows/speaking-live-voice-prod-e2e.yml (never locally).
import { chromium, devices } from 'playwright';
import fs from 'node:fs';

const APP = process.env.APP_URL ?? 'https://app.oetwithdrhesham.co.uk';
const { QA_EMAIL, QA_PASSWORD, CARD_ID, CANDIDATE_WAV, SPEAK_SECONDS = '110' } = process.env;
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
const page = await context.newPage();
const transcript = { candidate: '', patient: '' };
let providerError = null;
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
  await page.waitForURL(/\/speaking\/sessions\/[^/]+$/, { timeout: 60_000 });
  await page.getByTestId('speaking-mic-indicator').waitFor({ timeout: 60_000 });
  const start = page.getByRole('button', { name: 'Start speaking' });
  if (await start.isVisible().catch(() => false)) await start.click();
  await page.getByText(/Live — the patient is listening|Patient speaking/).waitFor({ timeout: 45_000 });
  log('live voice connected');
  await shot('3-active-live');
  await page.waitForTimeout(Number(SPEAK_SECONDS) * 1000);
  await shot('4-active-after-conversation');
  if (providerError) throw new Error(`Gemini reported: ${providerError}`);
  if (!transcript.patient.trim()) throw new Error('The AI patient never spoke.');
  await page.getByRole('button', { name: 'Finish & submit' }).click();
  await page.getByRole('button', { name: 'Submit now' }).click();
  await page.waitForURL(/\/results/, { timeout: 90_000 });
  log('submitted', page.url());
  const deadline = Date.now() + 6 * 60_000;
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
  if (!graded) throw new Error('No graded result within 6 minutes.');
} catch (error) {
  failed = error;
  await shot('failure').catch(() => undefined);
} finally {
  fs.writeFileSync(`${out}/transcript.json`, JSON.stringify(transcript, null, 2));
  log('CANDIDATE (as heard by Gemini):', transcript.candidate.trim().slice(0, 1500));
  log('PATIENT (Gemini Live):', transcript.patient.trim().slice(0, 3000));
  await browser.close();
}
if (failed) {
  console.error(failed);
  process.exit(1);
}
