import fs from 'node:fs';
import path from 'node:path';
import { test, expect } from '@playwright/test';

// Owner Rev8 §12.3 / §19.2 (Track A): the stored Model Answer spacing must
// survive storage, API transport, HTML rendering and the candidate-facing
// component. For every matrix cell this opens the REAL production result page
// as the QA learner, reads the rendered Grounded Model Answer (innerText
// honours CSS white-space) and screenshots it. The stored text comparison is
// done offline against the saved answer; this spec asserts the layout facts.

type Cell = { cell: string; submissionId: string; taskId: string };

const EMAIL = process.env.WRITING_REV8_EMAIL ?? '';
const PASSWORD = process.env.WRITING_REV8_PASSWORD ?? '';
const DEVICE_ID = 'oet-rev8-writing-qa-device-01';
const OUT = path.join('output', 'playwright', 'writing-rev8-render');

function loadCells(): Cell[] {
  const b64 = process.env.WRITING_REV8_CELLS_B64;
  if (b64) return JSON.parse(Buffer.from(b64, 'base64').toString('utf8'));
  return JSON.parse(fs.readFileSync(path.join('tests', 'e2e', 'fixtures', 'writing-rev8-render-cells.json'), 'utf8'));
}

test('Rev8 Track A: rendered Grounded Model Answer keeps the owner spacing', async ({ page }) => {
  test.skip(!EMAIL || !PASSWORD, 'WRITING_REV8_EMAIL / WRITING_REV8_PASSWORD not set');
  const cells = loadCells();
  fs.mkdirSync(OUT, { recursive: true });

  await page.addInitScript((deviceId) => window.localStorage.setItem('oet_device_id', deviceId), DEVICE_ID);
  await page.goto('/sign-in');
  await page.locator('input[name="email"]').fill(EMAIL);
  await page.locator('input[name="password"]').fill(PASSWORD);
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.waitForURL((url) => url.pathname !== '/sign-in', { timeout: 30_000 });

  const results: Array<Record<string, unknown>> = [];
  for (const c of cells) {
    const row: Record<string, unknown> = { ...c };
    try {
      await page.goto(`/writing/submissions/${c.submissionId}/results`);
      const answer = page.getByTestId('grounded-model-answer');
      await expect(answer).toBeVisible({ timeout: 45_000 });
      const rendered = await answer.innerText();
      const whiteSpace = await answer.evaluate((el) => getComputedStyle(el).whiteSpace);
      await answer.screenshot({ path: path.join(OUT, `cell-${c.cell}.png`) });
      const lines = rendered.replace(/\r/g, '').split('\n');
      const reIdx = lines.findIndex((l) => /^\s*Re\s*:/i.test(l));
      row.found = true;
      row.whiteSpace = whiteSpace;
      row.renderedText = rendered;
      row.salutationReConsecutive = reIdx > 0 && /^\s*Dear\b/i.test(lines[reIdx - 1]);
      row.blankAfterRe = reIdx >= 0 && lines[reIdx + 1]?.trim() === '' && (lines[reIdx + 2] ?? '').trim() !== '';
      row.paragraphBlocks = rendered.replace(/\r/g, '').split(/\n\s*\n/).filter((p) => p.trim()).length;
      row.pass = Boolean(row.salutationReConsecutive && row.blankAfterRe && (whiteSpace === 'pre-wrap' || whiteSpace === 'pre-line'));
    } catch (e) {
      row.found = false;
      row.pass = false;
      row.error = String(e).slice(0, 500);
    }
    results.push(row);
    console.log(`CELL ${c.cell} ${c.submissionId} pass=${row.pass} ws=${row.whiteSpace ?? '-'} blocks=${row.paragraphBlocks ?? '-'}`);
  }
  fs.writeFileSync(path.join(OUT, 'render-results.json'), JSON.stringify(results, null, 1));
  expect(results.filter((r) => !r.pass).map((r) => r.cell)).toEqual([]);
});
