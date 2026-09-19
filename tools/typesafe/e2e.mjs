#!/usr/bin/env node
/**
 * TypeSafe SystemOne (Jev) live E2E — the committed version of the session
 * scratchpad script that verified the first integration (2026-09-19).
 *
 * Sends the canonical OET-style referral state with one Choice + one Noul +
 * one Score question and asserts the expected judgments. Run it whenever the
 * key, model pin, or wire contract needs re-verification. Server-side only:
 * the key is read from the environment and never logged (masked report only).
 *
 * Usage (from OET Project Web App/):
 *   TYPESAFE_API_KEY=... node tools/typesafe/e2e.mjs
 *   TYPESAFE__APIKEY=... node tools/typesafe/e2e.mjs     (backend-style name)
 *
 * Env is read from process.env only — never CLI args, never files.
 */

const MODEL = 'jev-1.13.0'; // pinned — matches TypeSafeOptions.Model
const BASE_URL = process.env.TYPESAFE_BASE_URL || 'https://api.typesafe.ai';

const apiKey = process.env.TYPESAFE_API_KEY || process.env.TYPESAFE__APIKEY || '';
if (!apiKey) {
  console.error('FAIL: set TYPESAFE_API_KEY (or TYPESAFE__APIKEY) in the environment.');
  process.exit(2);
}

const letter = `Dear Dr Aisha Rahman,

Re: Mr Kenneth Osei, DO 14 March 1958

Mr Osei attended today reporting a two-day history of central chest pain,
radiating to his left jaw, associated with sweating and nausea. The pain
began at rest and lasted approximately forty minutes. He has a history of
hypertension and type 2 diabetes and takes metformin 1g twice daily.

On examination he appeared pale and clammy. Blood pressure 150/90 mmHg,
pulse 96 regular, oxygen saturation 96% on room air. Heart sounds normal,
chest clear. ECG shows ST elevation in leads II, III and aVF.

I suspect an inferior STEMI. I have given aspirin 300mg orally and admit
him to the acute medical unit. Please review him urgently regarding
primary PCI, as the cardiology registrar is currently unavailable.`;

const body = {
  state: { letter, task: 'urgent referral' },
  model: MODEL,
  questions: {
    letter_type: {
      type: 'choice',
      instructions:
        'Which OET letter type does `state.letter` represent? Judge only by the genre conventions present in the letter text.',
      criteria: {
        urgent_referral: 'Asks another clinician to take over or review a patient urgently; acute findings; time-critical language.',
        routine_referral: 'Asks another clinician to review or manage a patient without time-critical language.',
        discharge_summary: 'Summarises a hospital stay for ongoing care after discharge.',
        unclear: 'None of the above fit the letter text.',
      },
    },
    is_urgent: {
      type: 'noul',
      instructions:
        'Does `state.letter` convey clinical urgency that requires same-day action by the recipient? Answer yes only when the text itself is time-critical.',
      criteria: {
        true: 'The letter asks for same-day or immediate clinical action.',
        false: 'The letter contains no same-day action request.',
      },
    },
    clinical_detail: {
      type: 'score',
      instructions:
        'How much relevant clinical detail does `state.letter` contain (findings, observations, medication, investigation results)?',
      criteria: [
        'Sparse: most of findings, observations, medication, and results are missing.',
        'Partial: some relevant clinical detail present but key items missing.',
        'Rich: findings, observations, medication, and investigation results are all present.',
      ],
    },
  },
};

function mask(key) {
  return `${key.slice(0, 10)}…${key.slice(-4)} (len ${key.length})`;
}

async function main() {
  console.log(`TypeSafe E2E — key ${mask(apiKey)}, model ${MODEL}, base ${BASE_URL}`);
  const started = Date.now();
  const response = await fetch(`${BASE_URL}/v1/systemone`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${apiKey}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  const latencyMs = Date.now() - started;

  if (!response.ok) {
    console.error(`FAIL: HTTP ${response.status} ${response.statusText} in ${latencyMs}ms`);
    console.error(text.slice(0, 400));
    process.exit(1);
  }

  const result = JSON.parse(text);
  const letterType = result.answers?.letter_type;
  const isUrgent = result.answers?.is_urgent;
  const detail = result.answers?.clinical_detail;

  console.log(`HTTP 200 in ${latencyMs}ms; model ${result.model}; usage ${JSON.stringify(result.usage)}`);
  console.log(`letter_type=${letterType?.choice} (confidence ${letterType?.confidence})`);
  console.log(`is_urgent=${isUrgent?.noul}`);
  console.log(`clinical_detail=${detail?.score} (confidence ${detail?.confidence})`);

  const failures = [];
  if (result.model !== MODEL && result.model !== 'jev-latest') {
    failures.push(`unexpected model ${result.model}`);
  }
  if (letterType?.choice !== 'urgent_referral') failures.push(`letter_type expected urgent_referral, got ${letterType?.choice}`);
  if (!(isUrgent?.noul >= 0.9)) failures.push(`is_urgent expected >= 0.9, got ${isUrgent?.noul}`);
  if (!(detail?.score >= 1.5)) failures.push(`clinical_detail expected >= 1.5, got ${detail?.score}`);

  if (failures.length > 0) {
    console.error(`FAIL: ${failures.join('; ')}`);
    process.exit(1);
  }
  console.log('PASS');
}

main().catch((error) => {
  console.error(`FAIL: ${error?.message ?? error}`);
  process.exit(1);
});
