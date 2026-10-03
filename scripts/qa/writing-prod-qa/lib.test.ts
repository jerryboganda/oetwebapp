import doc from './scripts.json';
import { WRITING_PROFESSIONS } from '@/lib/writing/types';
import { CATEGORIES, HANDOFF_PROFESSIONS, PROVIDERS, TEST_IDS } from './contract.mjs';
import {
  buildTable, categoryForLetterType, contractGaps, correctionsProblems, createLane, creditPreflight, creditVerdict,
  deriveDeviceId, faultSideEffects, freeSampleProblems, gradingStepsProblems, guardDecision, letterTypeCode,
  myWorkProblems, normalizeProfessionId, overallVerdict, pacingDelayMs, paidSpendProblems, parseInputs,
  planDiscovery, planRows, preflightDecision, splitText, providerEvidence, reportTextProblems, scoreLabelProblems, sectionOrderProblems,
  syntheticEmail, timerVerdict, validateScripts, verdictOf, writingHealth,
} from './lib.mjs';
import {
  clearanceProblems, containmentProblems, mobileVerdict, overlapProblems, positiveControl, rectsIntersect, targetProblems,
} from './geometry.mjs';

const rect = (left: number, top: number, right: number, bottom: number) => ({ left, top, right, bottom });

describe('handoff scripts', () => {
  it('holds the 36 verbatim scripts, 3 per profession, typeable as one plain paragraph', () => {
    expect(validateScripts(doc)).toEqual({ ok: true, problems: [] });
    expect(doc.scripts).toHaveLength(36);
  });

  it('uses the product profession ids', () => {
    expect([...HANDOFF_PROFESSIONS].sort()).toEqual(WRITING_PROFESSIONS.filter((p) => p !== 'other').sort());
  });

  it('rejects editor input-rule triggers, extra paragraphs, non-ASCII and gaps', () => {
    const bad = (patch: Record<string, string>, index = 0) => ({
      scripts: doc.scripts.map((s, i) => (i === index ? { ...s, ...patch } : s)),
    });
    for (const text of ['Dear Dr Green, **urgent** review.', 'Dear Dr _Green_, review.', 'Dear Dr Green,\nPlease review.',
      'Dear Dr Green, review – now.', '- Dear Dr Green', 'Dr Green, please review.', 'Dear Dr Green, `x` review.']) {
      expect(validateScripts(bad({ text })).ok, text).toBe(false);
    }
    expect(validateScripts(bad({ profession: 'speech_pathology' })).ok).toBe(false);
    expect(validateScripts({ scripts: doc.scripts.slice(1) }).problems).toContain('missing script medicine/routine');
  });

  it('normalises profession ids and letter types the way the product does', () => {
    expect(normalizeProfessionId(' Speech_Pathology ')).toBe('speech-pathology');
    expect(normalizeProfessionId('veterinary_science')).toBe('veterinary');
    expect(letterTypeCode('urgent_referral')).toBe('LT-UR');
    expect(letterTypeCode('lt-dg')).toBe('LT-DG');
    expect(letterTypeCode('LT-RP')).toBe('LT-OT');
    expect(categoryForLetterType('LT-TR')).toBe('discharge');
    expect(categoryForLetterType('LT-UR')).toBe('urgent');
  });
});

describe('inputs', () => {
  it('defaults to a read-only discover run', () => {
    const inputs = parseInputs({});
    expect(inputs).toMatchObject({ suite: 'discover', readingWindow: 'real', concurrency: 3, paceSeconds: 35, faultMode: 'flag', verifyCredits: true, cleanup: 'on_success', preflightRepair: false });
    expect(inputs.professions).toEqual(HANDOFF_PROFESSIONS);
    expect(inputs.categories).toEqual(CATEGORIES);
  });

  it('refuses unknown values and pacing below the AiScoring limiter', () => {
    expect(() => parseInputs({ SUITE: 'everything' })).toThrow(/SUITE/);
    expect(() => parseInputs({ PACE_SECONDS: '20' })).toThrow(/PACE_SECONDS/);
    expect(() => parseInputs({ CONCURRENCY: '4' })).toThrow(/CONCURRENCY/);
    expect(() => parseInputs({ PROFESSIONS: 'medicine,surgery' })).toThrow(/surgery/);
    expect(() => parseInputs({ VERIFY_CREDITS: 'yes' })).toThrow(/VERIFY_CREDITS/);
    expect(parseInputs({ PROFESSIONS: 'Speech_Pathology', BROWSERS: 'chromium,webkit' })).toMatchObject({ professions: ['speech-pathology'], browsers: ['chromium', 'webkit'] });
  });
});

describe('discovery plan', () => {
  const task = (id: string, profession: string, letterType: string, ok = true) => ({ scenarioId: id, taskTitle: `Task ${id}`, profession, letterType, publishReady: ok, candidateVisible: true });
  const compatibility = {
    rows: [
      task('m1', 'medicine', 'LT-RR'), task('m2', 'medicine', 'LT-UR'), task('m3', 'medicine', 'LT-DG'), task('m4', 'medicine', 'LT-RR'),
      task('n1', 'nursing', 'LT-RR'), task('n2', 'nursing', 'LT-TR'), task('n3', 'nursing', 'LT-NM'),
      task('p1', 'pharmacy', 'LT-RR'), task('p2', 'pharmacy', 'LT-UR'), task('p3', 'pharmacy', 'LT-DG', false),
      task('o1', 'occupational_therapy', 'LT-RR'), task('o2', 'occupational_therapy', 'LT-UR'), task('o3', 'occupational_therapy', 'LT-DG'),
    ],
  };
  const integrity = { rows: compatibility.rows.map((r) => ({ scenarioId: r.scenarioId, loadOk: r.scenarioId !== 'm4' })) };
  const catalog = { professions: ['medicine', 'nursing', 'pharmacy', 'occupational-therapy'].map((id) => ({ id, label: id, isActive: id !== 'occupational-therapy' })) };
  const plan: any[] = planDiscovery({ catalog, compatibility, integrity });
  const of = (p: string): any => plan.find((x) => x.profession === p);

  it('keeps one entry per handoff profession and enables only active ones with 3 eligible tasks', () => {
    expect(plan.map((p: { profession: string }) => p.profession)).toEqual(HANDOFF_PROFESSIONS);
    expect(plan.filter((p: { enabled: boolean }) => p.enabled).map((p: { profession: string }) => p.profession)).toEqual(['medicine', 'nursing']);
    expect(of('dietetics').notes[0]).toMatch(/not an account profession/);
    expect(of('pharmacy').notes[0]).toMatch(/only 2 eligible/);
  });

  it('picks LT-RR / LT-UR / LT-DG first and falls back to distinct other categories', () => {
    expect(of('medicine').picks.map((p: { scenarioId: string }) => p.scenarioId)).toEqual(['m1', 'm2', 'm3']);
    const nursing = of('nursing').picks;
    expect(nursing.map((p: { letterType: string }) => p.letterType)).toEqual(['LT-RR', 'LT-NM', 'LT-TR']);
    expect(nursing[1].fallback).toMatch(/urgent script typed into LT-NM/);
  });

  it('never matches a variant spelling the learner library would not show', () => {
    expect(of('occupational-therapy').enabled).toBe(false);
    expect(of('occupational-therapy').evidence).toMatchObject({ catalogue: 'occupational-therapy (inactive)', vocabularyTasks: 3, exactMatchTasks: 0 });
    expect(of('occupational-therapy').notes.join(' ')).toMatch(/stored as "occupational_therapy"/);
  });

  it('turns the plan into QA-2 rows: planned letters and one NOT_ENABLED row per other profession', () => {
    const rows: any[] = planRows(plan, { medicine: 'Medicine' });
    expect(rows.filter((r) => r.status === 'NOT_RUN')).toHaveLength(6);
    expect(rows.filter((r) => r.status === 'NOT_ENABLED')).toHaveLength(10);
    expect(rows[0]).toMatchObject({ profession: 'Medicine', task: 'LT-RR m1', category: 'routine' });
    expect(rows.find((r) => r.profession === 'dietetics').notes).toMatch(/evidence \{"catalogue":"absent"/);
  });

  it('splits a script into chunks that type back the exact text', () => {
    const text = doc.scripts[0].text;
    const parts = splitText(text, 5);
    expect(parts).toHaveLength(5);
    expect(parts.join('')).toBe(text);
  });

  it('honours the profession and category filters', () => {
    const only: any[] = planDiscovery({ catalog, compatibility, integrity, professions: ['medicine'], categories: ['urgent'] });
    expect(only).toHaveLength(1);
    expect(only[0].picks.map((p: { scenarioId: string }) => p.scenarioId)).toEqual(['m2']);
  });
});

describe('credit rule', () => {
  const user = 'usr_1';
  const scenario = '11111111-2222-3333-4444-555555555555';
  const submission = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
  const grant = { id: 't0', reason: 'AdminAdjustment', referenceId: 'admin:x', writingOnlyCreditsDelta: 12 };
  const before = { writingOnlyCredits: 12, transactions: [grant] };
  const open = { id: 't1', reason: 'GradingDeduct', referenceId: `writing-v2:${user}:${scenario}:0`, writingOnlyCreditsDelta: -2 };
  const snapshot = (credits: number, ...rows: object[]) => ({ writingOnlyCredits: credits, transactions: [...rows, grant] });

  it('passes one 2-credit debit at task open with the hold adopted at grading', () => {
    const verdict = creditVerdict({ userId: user, scenarioId: scenario, submissionId: submission, before, after: snapshot(10, open),
      steps: [{ name: 'after task open', snapshot: snapshot(10, open), delta: -2 }] });
    expect(verdict).toMatchObject({ ok: true, delta: -2 });
  });

  it('fails a second debit at grading and a released hold', () => {
    const second = { id: 't2', reason: 'GradingDeduct', referenceId: `writing-grade:${submission.replaceAll('-', '')}`, writingOnlyCreditsDelta: -2 };
    const release = { id: 't3', reason: 'RefundOnFailure', referenceId: `writing-grade:${submission.replaceAll('-', '')}:release`, writingOnlyCreditsDelta: 2 };
    const double = creditVerdict({ userId: user, scenarioId: scenario, submissionId: submission, before, after: snapshot(8, second, open) });
    expect(double.problems.join(' ')).toMatch(/second debit/);
    expect(double.problems.join(' ')).toMatch(/moved by -4/);
    const released = creditVerdict({ userId: user, scenarioId: scenario, submissionId: submission, before, after: snapshot(10, release, second, open) });
    expect(released.problems.join(' ')).toMatch(/release\/refund/);
  });

  it('charges nothing for a free sample and 2 at grade time for a revision', () => {
    expect(creditVerdict({ kind: 'free_sample', userId: user, scenarioId: scenario, submissionId: submission, before, after: before }).ok).toBe(true);
    expect(creditVerdict({ kind: 'free_sample', userId: user, scenarioId: scenario, submissionId: submission, before, after: snapshot(10, open) }).ok).toBe(false);
    const revise = { id: 't4', reason: 'GradingDeduct', referenceId: `writing-grade:${submission.replaceAll('-', '')}`, writingOnlyCreditsDelta: -2 };
    expect(creditVerdict({ kind: 'revision', userId: user, scenarioId: scenario, submissionId: submission, before, after: snapshot(10, revise) }).ok).toBe(true);
  });

  it('refuses learners whose credit assertions would be vacuous', () => {
    const paid = { allowed: true, tier: 'ai_package', remaining: 6 };
    expect(creditPreflight(snapshot(12), paid, { letters: 3 })).toEqual([]);
    expect(creditPreflight({ ...snapshot(12), writingUnlimited: true }, paid, { letters: 3 })[0]).toMatch(/unlimited/);
    expect(creditPreflight(snapshot(12), { ...paid, remaining: null }, { letters: 3 }).join(' ')).toMatch(/unlimited/);
    expect(creditPreflight({ writingOnlyCredits: 0, transactions: [] }, paid, { letters: 1 }).join(' ')).toMatch(/legacy grading bypass/);
    expect(creditPreflight(snapshot(4), paid, { letters: 3 }).join(' ')).toMatch(/only 2 letter/);
    expect(creditPreflight({ writingOnlyCredits: 0, transactions: [] }, null, { letters: 1, kind: 'free' })).toEqual([]);
    expect(creditPreflight(snapshot(2), null, { letters: 1, kind: 'free' })[0]).toMatch(/holds credits/);
  });
});

describe('timer verdict (away 30 s, 8 s load / +6 s autosave tolerance)', () => {
  it('proves pause-while-away without a reset or a gain', () => {
    expect(timerVerdict({ before: 2300, after: 2296, window: 2400 }).ok).toBe(true);
    expect(timerVerdict({ before: 2300, after: 2400, window: 2400 }).problems.join(' ')).toMatch(/reset/);
    expect(timerVerdict({ before: 2300, after: 2270, window: 2400 }).problems.join(' ')).toMatch(/ran while away/);
    expect(timerVerdict({ before: 2300, after: 2310, window: 2400 }).problems.join(' ')).toMatch(/gained/);
    expect(timerVerdict({ before: 2300, after: Number.NaN, window: 2400 }).ok).toBe(false);
  });

  it('keeps a mounted (offline) clock running', () => {
    expect(timerVerdict({ before: 2300, after: 2282, window: 2400, mode: 'running', elapsed: 20 }).ok).toBe(true);
    expect(timerVerdict({ before: 2300, after: 2299, window: 2400, mode: 'running', elapsed: 20 }).ok).toBe(false);
  });
});

describe('provider evidence', () => {
  const row = (providerId: string, outcome = 'Success', at = 0, extra = {}) => ({ providerId, outcome, model: `${providerId}-model`, createdAt: new Date(1_000_000 + at).toISOString(), failoverTrace: null, ...extra });

  it('passes a letter graded by the Max subscription first', () => {
    const e = providerEvidence({ usage: [row(PROVIDERS.claude)], modelUsed: 'claude-opus-5-5' });
    expect(e).toMatchObject({ firstProvider: PROVIDERS.claude, finalProvider: PROVIDERS.claude, fallback: false, paidApiCalls: 0, problems: [] });
  });

  it('fails max_not_first and reports fallback when the first hop is not Max', () => {
    const e = providerEvidence({ usage: [row(PROVIDERS.codex, 'Success', 5)], modelUsed: 'gpt-6.1-sol' });
    expect(e.problems[0]).toMatch(/^max_not_first/);
    expect(e.fallback).toBe(true);
  });

  it('accepts Codex first only for a deliberate L1+L2 fault run', () => {
    const e = providerEvidence({ usage: [row(PROVIDERS.codex)], modelUsed: 'gpt-6.1-sol', expectedFirst: PROVIDERS.codex });
    expect(e.problems).toEqual([]);
    expect(e.fallback).toBe(true);
  });

  it('flags paid API spend, a failed hop and the empty-letter model', () => {
    const e = providerEvidence({ usage: [row(PROVIDERS.api, 'Success', 10), row(PROVIDERS.claude, 'ProviderError', 0)], modelUsed: 'deterministic-empty-v1' });
    expect(e.paidApiCalls).toBe(1);
    expect(e.fallbackReasons).toEqual(expect.arrayContaining(['a non-success grading call', 'more than one provider']));
    expect(e.problems.join(' ')).toMatch(/INCIDENT/);
    expect(e.problems.join(' ')).toMatch(/deterministic-empty-v1/);
    expect(providerEvidence({ usage: [], modelUsed: undefined }).problems[0]).toMatch(/no writing.grade usage/);
  });

  it('accepts a reused grade (no AI call) only when graded with a real model', () => {
    const reused = providerEvidence({ usage: [], modelUsed: 'claude-opus-5-5', graded: true });
    expect(reused).toMatchObject({ reused: true, problems: [], calls: 0 });
    expect(reused.note).toMatch(/grade reused for identical text/);
    expect(providerEvidence({ usage: [], modelUsed: 'claude-opus-5-5', graded: false }).problems[0]).toMatch(/no writing.grade usage/);
    expect(providerEvidence({ usage: [], modelUsed: 'deterministic-empty-v1', graded: true }).problems.join(' ')).toMatch(/no writing.grade usage/);
    expect(providerEvidence({ usage: [], modelUsed: null, graded: true }).problems[0]).toMatch(/no writing.grade usage/);
  });

  it('counts paid spend per run with zero expected', () => {
    expect(paidSpendProblems(0)).toEqual([]);
    expect(paidSpendProblems(2)[0]).toMatch(/zero expected/);
  });
});

describe('preflight and guard', () => {
  const now = Date.parse('2026-10-02T12:00:00Z');
  const circuit = (key: string, state: string, openUntil: string | null) => ({ kind: 'provider', key, state, openUntil });
  const clean = { quotaExceededUntil: null, failoverActive: false };
  const health = (writingProvider: object, rows: object[]) => writingHealth({ writingProvider, circuits: { rows }, now });

  it('starts on a clean chain and ignores circuits of other features', () => {
    const h = health(clean, [circuit('openai', 'open', '2026-10-03T00:00:00Z')]);
    expect(preflightDecision({ health: h, providers: [] })).toMatchObject({ ok: true, failures: [], blockers: [] });
  });

  it('fails a retired quota marker and a non-closed Max circuit (never repaired)', () => {
    const marked = preflightDecision({ health: health({ quotaExceededUntil: '2026-10-07T00:00:00Z', failoverActive: true }, []), providers: [], repair: true });
    expect(marked.failures.join(' ')).toMatch(/quota marker is retired/);
    expect(marked.repairs).toEqual([]);
    const staleMax = preflightDecision({ health: health(clean, [circuit(PROVIDERS.claude, 'open', '2026-10-01T00:00:00Z')]), providers: [], repair: true });
    expect(staleMax.failures.join(' ')).toMatch(/Max circuits are exempt/);
  });

  it('warns on a stale L2/L3 circuit, blocks a live one, and repairs both only when asked', () => {
    const stale = health(clean, [circuit(PROVIDERS.api, 'open', '2026-10-01T00:00:00Z')]);
    expect(preflightDecision({ health: stale, providers: [] })).toMatchObject({ ok: true, warnings: [expect.stringMatching(/stale/)] });
    const live = health(clean, [circuit(PROVIDERS.codex, 'open', '2026-10-02T13:00:00Z')]);
    expect(preflightDecision({ health: live, providers: [] })).toMatchObject({ ok: false, blockers: [expect.stringMatching(/preflight_repair/)] });
    expect(preflightDecision({ health: live, providers: [], repair: true })).toMatchObject({ ok: false, repairs: [PROVIDERS.codex] });
  });

  it('blocks an active L2 row when require_l2_disabled and a projected utilisation breach', () => {
    const h = health(clean, []);
    expect(preflightDecision({ health: h, providers: [{ code: 'anthropic', isActive: true }], requireL2Disabled: true }).blockers[0]).toMatch(/L2/);
    const writingProvider = { failoverPct: 90, quota: { weeklyTokenCap: 1_000_000, weeklyTokensUsed: 800_000 } };
    expect(preflightDecision({ health: h, providers: [], writingProvider, plannedLetters: 5 }).blockers[0]).toMatch(/projected/);
  });

  it('halts on incidents, pauses for a deploy, and checks fault side effects', () => {
    const ok = health(clean, []);
    expect(guardDecision({ deployBusy: false, health: ok })).toEqual({ action: 'continue', reasons: [] });
    expect(guardDecision({ deployBusy: true, health: ok }).action).toBe('pause');
    expect(guardDecision({ deployBusy: true, health: ok, anthropicRows: [{}] })).toMatchObject({ action: 'halt' });
    expect(guardDecision({ deployBusy: false, health: health({ quotaExceededUntil: '2026-10-07T00:00:00Z', failoverActive: true }, []) }).action).toBe('halt');
    const opened = health(clean, [circuit(PROVIDERS.codex, 'open', '2026-10-02T13:00:00Z')]);
    expect(faultSideEffects(ok, opened)[0]).toMatch(/opened by the fault/);
    expect(faultSideEffects(ok, ok)).toEqual([]);
  });
});

describe('lane mutex and pacing', () => {
  it('lets exactly one submission grade at a time, FIFO, surviving a failure', async () => {
    const lane = createLane();
    const order: string[] = [];
    const job = (name: string, ms: number, fail = false) => lane.run(async () => {
      order.push(`start ${name}`);
      await new Promise((r) => setTimeout(r, ms));
      order.push(`end ${name}`);
      if (fail) throw new Error(name);
      return name;
    });
    const results = await Promise.allSettled([job('a', 20), job('b', 5, true), job('c', 1)]);
    expect(order).toEqual(['start a', 'end a', 'start b', 'end b', 'start c', 'end c']);
    expect(results.map((r) => r.status)).toEqual(['fulfilled', 'rejected', 'fulfilled']);
    expect(lane.maxActive).toBe(1);
  });

  it('spaces a learner\'s scoring calls by the pace', () => {
    expect(pacingDelayMs(null, 1000, 35)).toBe(0);
    expect(pacingDelayMs(1000, 11_000, 35)).toBe(25_000);
    expect(pacingDelayMs(1000, 60_000, 35)).toBe(0);
  });
});

describe('report and post-submission checks', () => {
  it('requires the contract ids', () => {
    expect(contractGaps('editor', [TEST_IDS.editor, TEST_IDS.timer])).toEqual([TEST_IDS.draftStatus, TEST_IDS.submit]);
  });

  it('reads the grading step exactly', () => {
    expect(gradingStepsProblems(['Reading your letter', 'Preparing model answer'])).toEqual([]);
    expect(gradingStepsProblems(['Preparing reference exemplar'])).toHaveLength(2);
  });

  it('checks the report order, the corrections list and broken text', () => {
    expect(sectionOrderProblems(['score', 'priorities', 'model-answer', 'criteria', 'corrections', 'reference', 'next-actions'])).toEqual([]);
    expect(sectionOrderProblems(['score', 'criteria', 'priorities', 'model-answer', 'corrections', 'next-actions'])[0]).toMatch(/order/);
    expect(sectionOrderProblems(['score', 'priorities', 'criteria', 'corrections', 'next-actions'])[0]).toMatch(/model-answer/);
    expect(correctionsProblems({ preview: 5, full: 12, api: 12, expandable: true })).toEqual([]);
    expect(correctionsProblems({ preview: 4, full: 4, api: 4, expandable: false })).toEqual([]);
    expect(correctionsProblems({ preview: 5, full: 11, api: 12, expandable: true })).toHaveLength(1);
    expect(correctionsProblems({ preview: 12, full: 12, api: 12, expandable: false })[0]).toMatch(/no "View all corrections"/);
    expect(correctionsProblems({ preview: 3, full: 12, api: 12, expandable: true })[0]).toMatch(/preview shows 3/);
    expect(correctionsProblems({ preview: 4, full: 4, api: 4, expandable: true })[0]).toMatch(/only 4/);
    expect(reportTextProblems('Score 300/500 NaN', ['/writing/submissions/x/appeal'])).toHaveLength(2);
    expect(reportTextProblems('Appeal score')).toHaveLength(1);
    expect(scoreLabelProblems('342/500')).toEqual([]);
    expect(scoreLabelProblems('undefined/500')).toHaveLength(1);
  });

  it('expects the submission once in Post Submissions and a free sample counted once', () => {
    expect(myWorkProblems({ items: [{ submissionId: 'A-1', state: 'graded' }], submissionId: 'a1', state: 'graded' })).toEqual([]);
    expect(myWorkProblems({ items: [], submissionId: 'a1', state: 'graded' })[0]).toMatch(/0 times/);
    expect(myWorkProblems({ items: [{ submissionId: 'a1', state: 'failed' }], submissionId: 'a1', state: 'graded' })[0]).toMatch(/failed/);
    expect(freeSampleProblems({ afterFailure: [{ state: 'grading_failed', successfulCount: 0 }], afterRetry: [{ state: 'available', successfulCount: 1 }] })).toEqual([]);
    expect(freeSampleProblems({ afterFailure: [{ state: 'completed', successfulCount: 1 }], afterRetry: [] })).toHaveLength(2);
  });
});

describe('statuses and the QA-2 table', () => {
  const row = (status: string, extra = {}) => ({ profession: 'Medicine', task: 'LT-RR / abc', category: 'Routine', saved: 'yes', provider: 'writing-claude-sub', fallback: 'no', status, notes: 'ok', ...extra });

  it('derives a row status: blocked > fail > partial > pass', () => {
    expect(verdictOf({})).toBe('PASS');
    expect(verdictOf({ partials: ['x'] })).toBe('PARTIAL');
    expect(verdictOf({ partials: ['x'], problems: ['y'] })).toBe('FAIL');
    expect(verdictOf({ problems: ['y'], blocked: ['z'] })).toBe('BLOCKED');
  });

  it('builds the markdown and CSV with the plan columns, escaping cells', () => {
    const { md, csv } = buildTable([row('PASS', { notes: 'a | b, "c"' }), row('PARTIAL', { notes: 'UI proven live' })], { runId: '42' });
    expect(md.split('\n')[0]).toBe('| Profession | Task/ID | Category | Saved in Post Submissions | Provider used | Fallback? | Final result | Notes |');
    expect(md).toContain('a \\| b, "c"');
    expect(md).toContain('UI proven live [proven live in run 42]');
    expect(csv.split('\n')[1]).toContain('"a | b, ""c"""');
    expect(() => buildTable([row('MAYBE')])).toThrow(/unknown status/);
  });

  it('never prints ALL PASS unless every test row passed', () => {
    expect(overallVerdict([row('PASS'), row('PASS'), row('NOT_ENABLED', { profession: 'Dietetics' })])).toBe('ALL PASS (2/2 tests; NOT_ENABLED: Dietetics)');
    expect(overallVerdict([row('PASS'), row('PARTIAL')])).toBe('NOT ALL PASS (1 PASS, 1 PARTIAL)');
    expect(overallVerdict([row('NOT_ENABLED')])).toMatch(/^NOT ALL PASS \(no tests/);
    for (const status of ['FAIL', 'NOT_RUN', 'BLOCKED', 'VOID_DEPLOY']) expect(overallVerdict([row('PASS'), row(status)])).toMatch(/^NOT ALL PASS/);
  });

  it('derives stable synthetic identities', () => {
    expect(deriveDeviceId('run-1:medicine')).toBe(deriveDeviceId('run-1:medicine'));
    expect(deriveDeviceId('run-1:medicine')).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-a[0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(deriveDeviceId('run-1:medicine')).not.toBe(deriveDeviceId('run-1:nursing'));
    expect(syntheticEmail('123-1', 'medicine_acc')).toBe('wqa-123-1-medicine-acc@oetwithdrhesham.co.uk');
  });
});

describe('geometry detectors', () => {
  const card = rect(100, 100, 400, 300);
  const doc = { scrollWidth: 1366, clientWidth: 1366 };

  it('passes contained text and flags text, tiles and pages that overflow', () => {
    const ok = { found: true, card, texts: [{ text: '342/500', rect: rect(110, 110, 200, 130) }], scroll: [{ name: 'card', scrollWidth: 300, clientWidth: 300 }], doc };
    expect(containmentProblems(ok)).toEqual([]);
    expect(containmentProblems({ ...ok, texts: [{ text: 'Occupational Therapy', rect: rect(110, 110, 430, 130) }] })[0]).toMatch(/sticks out/);
    expect(containmentProblems({ ...ok, scroll: [{ name: 'tile 1', scrollWidth: 340, clientWidth: 300 }] })[0]).toMatch(/tile 1 overflows/);
    expect(containmentProblems({ ...ok, doc: { scrollWidth: 1400, clientWidth: 1366 } })[0]).toMatch(/scrolls horizontally/);
    expect(containmentProblems({ found: false })[0]).toMatch(/not found/);
  });

  it('flags content under the bottom nav or the handle and too little end clearance', () => {
    const nav = { name: 'bottom nav', rect: rect(8, 780, 382, 836) };
    const handle = { name: 'handle', rect: rect(340, 500, 390, 540) };
    expect(rectsIntersect(rect(0, 0, 10, 10), rect(10, 0, 20, 10))).toBe(false);
    const items = [{ kind: 'text', label: 'View all corrections', rect: rect(20, 770, 200, 790) }, { kind: 'control', label: 'Retry', rect: rect(300, 510, 380, 530) }];
    expect(overlapProblems({ obstacles: [nav, handle], items })).toHaveLength(2);
    expect(overlapProblems({ obstacles: [nav, handle], items }, ['handle'])).toEqual(['control "Retry" is under the handle']);
    expect(clearanceProblems({ lastContentBottom: 775, obstacles: [nav] })[0]).toMatch(/5 px above/);
    expect(clearanceProblems({ lastContentBottom: 760, obstacles: [nav] })).toEqual([]);
  });

  it('requires each target on screen, top-most and clear', () => {
    const viewport = { width: 390, height: 844 };
    const nav = { name: 'bottom nav', rect: rect(8, 780, 382, 836) };
    expect(targetProblems([{ label: 'Submit', found: true, rect: rect(20, 600, 200, 640), viewport, topmost: true }], [nav])).toEqual([]);
    expect(targetProblems([{ label: 'Submit', found: true, rect: rect(20, 790, 200, 830), viewport, topmost: false }], [nav])).toHaveLength(2);
    expect(targetProblems([{ label: 'View all corrections', found: false }], [])[0]).toMatch(/not found/);
  });

  it('never passes an unproven native-shell emulation', () => {
    expect(positiveControl({ width: 390, handleVisible: false, menuEntries: 2 })).toBe('PROVEN');
    expect(positiveControl({ width: 390, handleVisible: true, menuEntries: 1 })).toBe('NOT_PROVEN');
    expect(positiveControl({ width: 1366, handleVisible: true, menuEntries: 0 })).toBe('PROVEN');
    expect(positiveControl({ width: 1366, handleVisible: false, menuEntries: 2 })).toBe('NOT_PROVEN');
    expect(mobileVerdict({ control: 'NOT_PROVEN', problems: [] })).toBe('NOT_PROVEN');
    expect(mobileVerdict({ control: 'PROVEN', problems: [] })).toBe('PASS');
    expect(mobileVerdict({ control: 'NOT_PROVEN', problems: ['x'] })).toBe('FAIL');
  });
});
