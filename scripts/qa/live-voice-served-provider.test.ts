import {
  createServedRecord,
  creditPreflight,
  creditVerdict,
  gradeRetryVerdict,
  historyVerdict,
  isNavigationAbortNoise,
  learnerIdFromBearer,
  nearestRank,
  pinHonoured,
  pinIgnoredMessage,
  rawBandCodes,
  recordingMentions,
  resultsWordingVerdict,
} from './live-voice-served-provider.mjs';

type Panel = { provider: string | null; failedOver?: boolean } | undefined;
type Word = { at: number; who: 'candidate' | 'patient'; text: string };

// One synthetic run: what the panels reported, which create calls answered 2xx, and what the Gemini socket frames carried.
function run({ panels = [], created = [], words = [], errors = [] }: {
  panels?: Panel[];
  created?: string[];
  words?: Word[];
  errors?: string[];
}) {
  const transcript = { candidate: '', patient: '' };
  const stability = { providerErrors: [] as string[], sessionClosed: [] as unknown[] };
  const record = createServedRecord({
    panels: () => panels,
    servedCalls: () => created,
    gemini: { words, errors },
    transcript,
    stability,
  });
  return { ...record, transcript, stability };
}

const event = (type: string, delta: string | undefined, at: number, extra: Record<string, unknown> = {}) => ({ type, delta, __at: at, ...extra });

describe('live voice E2E: which provider served', () => {
  it('counts only Gemini when it went live, although an OpenAI leg left an error and text behind', () => {
    const r = run({
      panels: [{ provider: 'gemini', failedOver: true }],
      created: ['openai/offer', 'gemini/token'],
      words: [
        { at: 10, who: 'candidate', text: 'Hello doctor.' },
        { at: 20, who: 'patient', text: ' Hi.' },
      ],
    });

    r.applyServed([event('error', undefined, 1, { message: 'quota' }), event('session.output_transcript.delta', 'STALE', 2)]);

    expect(r.transcript).toEqual({ candidate: 'Hello doctor.', patient: ' Hi.' });
    expect(r.stability.providerErrors).toEqual([]);
    expect(r.openAiServed()).toBe(false);
    expect(r.geminiServed()).toBe(true);
  });

  it('counts only OpenAI when it went live, although an earlier Gemini leg left an error frame and text behind', () => {
    const r = run({
      panels: [{ provider: 'openai', failedOver: true }],
      created: ['gemini/token', 'openai/offer'],
      words: [{ at: 1, who: 'patient', text: 'STALE' }],
      errors: ['{"code":1}'],
    });

    r.applyServed([
      event('session.input_transcript.delta', 'How are ', 10),
      event('session.input_transcript.delta', 'you?', 11),
      event('session.output_transcript.delta', 'Fine.', 12),
      event('session.closed', undefined, 13, { reason: 'ended', usage: { seconds: 5 } }),
    ]);

    expect(r.transcript).toEqual({ candidate: 'How are you?', patient: 'Fine.' });
    expect(r.stability.providerErrors).toEqual([]);
    expect(r.stability.sessionClosed).toEqual([{ reason: 'ended', usage: { seconds: 5 } }]);
    expect(r.geminiServed()).toBe(false);
  });

  it('a genuine error event of the serving OpenAI leg is still reported', () => {
    const r = run({ panels: [{ provider: 'openai' }] });

    r.applyServed([event('session.input_transcript.delta', 'Hi', 1), event('error', undefined, 2, { message: 'boom' })]);

    expect(r.stability.providerErrors).toHaveLength(1);
    expect(r.stability.providerErrors[0]).toContain('boom');
  });

  it('keeps both providers of a mixed exam, in time order', () => {
    const r = run({
      panels: [{ provider: 'openai' }, { provider: 'gemini' }],
      created: ['openai/offer', 'gemini/token'],
      words: [
        { at: 100, who: 'candidate', text: 'Card B question. ' },
        { at: 110, who: 'patient', text: 'Card B answer.' },
      ],
      errors: ['{"g":1}'],
    });

    r.applyServed([
      event('session.input_transcript.delta', 'Card A question. ', 10),
      event('session.output_transcript.delta', 'Card A answer. ', 20),
      event('error', undefined, 30),
    ]);

    expect(r.transcript).toEqual({
      candidate: 'Card A question. Card B question. ',
      patient: 'Card A answer. Card B answer.',
    });
    expect(r.stability.providerErrors).toHaveLength(2);
    expect(r.openAiServed() && r.geminiServed()).toBe(true);
  });

  it('reads OpenAI timed words from the data-channel transcript deltas only', () => {
    const r = run({ panels: [{ provider: 'openai' }] });

    expect(r.openAiWords([
      event('session.input_transcript.delta', 'x', 5, { start_ms: 1, end_ms: 2 }),
      event('session.output_transcript.delta', 'y', 6),
      event('session.closed', undefined, 7),
    ])).toEqual([
      { at: 5, who: 'candidate', text: 'x', startMs: 1, endMs: 2 },
      { at: 6, who: 'patient', text: 'y', startMs: null, endMs: null },
    ]);
  });

  it('falls back to the create calls that succeeded when the build reports no provider on its panel', () => {
    const r = run({
      panels: [{ provider: null }],
      created: ['gemini/token'],
      words: [{ at: 1, who: 'patient', text: 'Hi' }],
    });

    expect(r.reportedProviders()).toEqual([]);
    expect(r.servedProviders()).toEqual(['gemini']);
    r.applyServed([event('session.output_transcript.delta', 'IGNORED', 2)]);
    expect(r.transcript.patient).toBe('Hi');
  });

  it('attributes nothing when nothing served', () => {
    const r = run({});

    r.applyServed([event('session.output_transcript.delta', 'x', 1)]);

    expect(r.servedProviders()).toEqual([]);
    expect(r.transcript).toEqual({ candidate: '', patient: '' });
  });
});

describe('live voice E2E: console noise from the harness own navigations', () => {
  const noise = [
    "[2026-10-01T00:15:43.098Z] Error: Failed to start the transport 'LongPolling': TypeError: Failed to fetch",
    "[2026-10-01T00:15:43.098Z] Error: Failed to start the connection: Error: Unable to connect to the server with any of the available transports. WebSockets failed: Error: 'WebSockets' is disabled by the client. ServerSentEvents failed: Error: 'ServerSentEvents' is disabled by the client. Error: LongPolling failed: TypeError: Failed to fetch",
    "[AI Assistant] Connection failed: c: Unable to connect to the server with any of the available transports. LongPolling failed: TypeError: Failed to fetch\n    at G._createTransport (https://app.example/_next/static/chunks/a.js:1:45400)",
    '[2026-09-30T21:45:42.439Z] Error: Failed to complete negotiation with the server: TypeError: Failed to fetch',
    "[2026-09-30T21:31:25.640Z] Error: Connection disconnected with error 'TypeError: Failed to fetch'.",
  ];

  it.each(noise)('treats an aborted AI Assistant connect as noise: %s', (text) => {
    expect(isNavigationAbortNoise(text)).toBe(true);
  });

  it.each([
    ['a page script error', "TypeError: Cannot read properties of undefined (reading 'segments')"],
    ['a failed product request', 'Error: Failed to fetch /v1/speaking/sessions/s1/transcript'],
    ['the assistant hub refusing the token', '[AI Assistant] Connection failed: Error: Unauthorized'],
    ['a refused connection that is not an abort', 'Error: Failed to start the connection: Error: Unauthorized'],
  ])('still reports %s', (_label, text) => {
    expect(isNavigationAbortNoise(text)).toBe(false);
  });
});

describe('live voice E2E: was the QA provider pin honoured', () => {
  const answer = (over: Record<string, unknown> = {}) => ({ requested: 'gemini', status: 200, pinned: true, provider: 'gemini', ...over });

  it('is null when no provider was requested, whatever the preflights said', () => {
    expect(pinHonoured([], '')).toBeNull();
    expect(pinHonoured([answer()], '')).toBeNull();
  });

  it('is true when every answered preflight pinned the provider that was asked for', () => {
    expect(pinHonoured([answer(), answer()], 'gemini')).toBe(true);
  });

  it('is false when the server ignored the pin, even if the primary happened to be the provider asked for', () => {
    expect(pinHonoured([answer({ requested: 'openai', pinned: false, provider: 'openai' })], 'openai')).toBe(false);
  });

  it('is false when no preflight carried provider=, or any answer was pinned to another provider', () => {
    expect(pinHonoured([], 'gemini')).toBe(false);
    expect(pinHonoured([answer(), answer({ provider: 'openai' })], 'gemini')).toBe(false);
  });

  it('ignores an answer that was not HTTP 200 (the page asks again), but needs at least one that was', () => {
    const limited = answer({ status: 429, pinned: false, provider: null });
    expect(pinHonoured([limited, answer()], 'gemini')).toBe(true);
    expect(pinHonoured([limited], 'gemini')).toBe(false);
  });

  it('names the feature flag key in the fail-fast message', () => {
    expect(pinIgnoredMessage('gemini', 'lrn_123')).toContain('speaking_live_voice_pin:lrn_123');
    expect(pinIgnoredMessage('gemini', 'lrn_123')).toContain('voice_provider=gemini');
    expect(pinIgnoredMessage('openai', null)).toContain('speaking_live_voice_pin:<learner user id>');
  });
});

describe('live voice E2E: the learner id in the sign-in token', () => {
  const b64url = (value: unknown) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');

  it('reads the sub claim, with or without the Bearer prefix', () => {
    const token = `header.${b64url({ sub: 'lrn_123', role: 'learner' })}.signature`;
    expect(learnerIdFromBearer(`Bearer ${token}`)).toBe('lrn_123');
    expect(learnerIdFromBearer(token)).toBe('lrn_123');
  });

  it('is null for a missing token, a token that is not a JWT and a payload without sub', () => {
    expect(learnerIdFromBearer(null)).toBeNull();
    expect(learnerIdFromBearer('Bearer abc')).toBeNull();
    expect(learnerIdFromBearer(`h.${b64url({ foo: 1 })}.s`)).toBeNull();
  });
});

describe('live voice E2E: may the credit run start', () => {
  const now = Date.parse('2026-10-01T00:00:00Z');
  const funded = {
    speakingUnlimited: false, mockExamsRemaining: 0, availableSpeakingActivities: 23,
    sharedCredits: 10, flexibleCredits: 20, speakingOnlyCredits: 16, writingOnlyCredits: 0, expiresAt: '2027-01-01T00:00:00Z',
  };

  it('goes ahead for a funded learner', () => {
    expect(creditPreflight(funded, { activities: 2, exam: true, now })).toEqual([]);
    expect(creditPreflight(funded, { activities: 1, now })).toEqual([]);
  });

  it('refuses unlimited Speaking: nothing would be charged', () => {
    const reasons = creditPreflight({ ...funded, speakingUnlimited: true }, { activities: 2, exam: true, now });
    expect(reasons).toHaveLength(1);
    expect(reasons[0]).toContain('unlimited Speaking');
  });

  it('refuses an exam when a mock exam unit would pay for it, but not a practice card', () => {
    const mock = { ...funded, mockExamsRemaining: 1 };
    expect(creditPreflight(mock, { activities: 2, exam: true, now })).toHaveLength(1);
    expect(creditPreflight(mock, { activities: 1, exam: false, now })).toEqual([]);
  });

  it('refuses when too few Speaking activities are fundable, using the server count', () => {
    expect(creditPreflight({ ...funded, availableSpeakingActivities: 1 }, { activities: 2, exam: true, now }))
      .toEqual(['only 1 Speaking activities can be funded and 2 are needed']);
    expect(creditPreflight({ ...funded, availableSpeakingActivities: 1 }, { activities: 1, now })).toEqual([]);
  });

  it('counts the pools that can pay for Speaking when an older server omits the count (Writing-only credits cannot)', () => {
    const old = { sharedCredits: 3, flexibleCredits: 0, speakingOnlyCredits: 0, writingOnlyCredits: 9 };
    expect(creditPreflight(old, { activities: 2, exam: true, now })).toEqual(['only 1 Speaking activities can be funded and 2 are needed']);
  });

  it('refuses credits that expire within two days, and an unreadable balance', () => {
    expect(creditPreflight({ ...funded, expiresAt: '2026-10-02T00:00:00Z' }, { activities: 2, exam: true, now })).toEqual(['the credits expire within two days']);
    expect(creditPreflight(null, { activities: 2, exam: true, now })).toEqual(['the credit balance could not be read']);
  });
});

describe('live voice E2E: was the run charged exactly once', () => {
  const snap = (speaking: number, transactions: unknown[] = []) => ({
    sharedCredits: 10, flexibleCredits: 0, speakingOnlyCredits: speaking, writingOnlyCredits: 0, transactions,
  });
  const debit = (referenceId: string, delta = -2) => ({ referenceId, reason: 'GradingDeduct', speakingOnlyCreditsDelta: delta });
  const purchase = { referenceId: 'purchase:1', reason: 'Purchase', sharedCreditsDelta: 10 };
  const A = 'exam:spx_1:cardA';
  const B = 'exam:spx_1:cardB';
  const exam = { prefix: 'exam:spx_1:', expectedRefs: [A, B] };

  it('accepts a clean exam: -4 in total, one 2-credit row per card, nothing refunded', () => {
    const verdict = creditVerdict({
      ...exam,
      before: snap(20),
      after: snap(16, [debit(B), debit(A), purchase]),
      steps: [
        { name: 'afterCardAHold', snapshot: snap(18), delta: -2 },
        { name: 'afterCardBHold', snapshot: snap(16), delta: -4 },
        { name: 'afterGrade', snapshot: snap(16), delta: -4 },
      ],
    });
    expect(verdict.problems).toEqual([]);
    expect(verdict.ok).toBe(true);
    expect(verdict.delta).toBe(-4);
    expect(verdict.rows).toEqual([
      { referenceId: B, reason: 'GradingDeduct', delta: -2 },
      { referenceId: A, reason: 'GradingDeduct', delta: -2 },
    ]);
  });

  it('accepts a clean practice card: -2 on its own reference', () => {
    const verdict = creditVerdict({ before: snap(20), after: snap(18, [debit('practice:sps_1')]), prefix: 'practice:sps_1', expectedRefs: ['practice:sps_1'] });
    expect(verdict.ok).toBe(true);
    expect(verdict.delta).toBe(-2);
  });

  it('accepts a free-sample card: nothing charged and no rows', () => {
    expect(creditVerdict({ before: snap(20), after: snap(20), prefix: 'practice:sps_2', expectedRefs: [] }).ok).toBe(true);
  });

  it('fails a card charged twice: a duplicate row and -6', () => {
    const verdict = creditVerdict({ ...exam, before: snap(20), after: snap(14, [debit(A), debit(A), debit(B)]) });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems.join('\n')).toMatch(/debit rows/);
    expect(verdict.problems.join('\n')).toContain('the balance moved by -6, expected -4');
  });

  it('fails a refund row, and a debit of the wrong size', () => {
    const refunded = creditVerdict({
      ...exam,
      before: snap(20),
      after: snap(18, [debit(A), debit(B), { referenceId: `${A}:release`, reason: 'RefundOnFailure', speakingOnlyCreditsDelta: 2 }]),
    });
    expect(refunded.ok).toBe(false);
    expect(refunded.problems).toContain('a refund row exists');
    const small = creditVerdict({ ...exam, before: snap(20), after: snap(17, [debit(A, -1), debit(B, -2)]) });
    expect(small.problems).toContain('a debit row is not exactly 2 credits');
  });

  it('fails an exam a mock exam unit paid for: no credits moved', () => {
    const verdict = creditVerdict({
      ...exam,
      before: snap(20),
      after: snap(20, [{ referenceId: 'exam:spx_1:mock', reason: 'MockDeduct', mockExamsDelta: -1 }]),
    });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems).toContain('a mock exam unit paid, not credits');
  });

  it('fails a card missing its row', () => {
    const verdict = creditVerdict({ ...exam, before: snap(20), after: snap(18, [debit(A)]) });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems.join('\n')).toMatch(/debit rows/);
  });

  it('fails a step where the balance was not where the hold said it should be, or could not be read', () => {
    const verdict = creditVerdict({
      ...exam,
      before: snap(20),
      after: snap(16, [debit(A), debit(B)]),
      steps: [
        { name: 'afterCardAHold', snapshot: snap(20), delta: -2 },
        { name: 'afterGrade', snapshot: undefined, delta: -4 },
      ],
    });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems).toContain('afterCardAHold: the balance moved by 0, expected -2');
    expect(verdict.problems).toContain('afterGrade: the balance could not be read');
  });

  it('is not ok when a balance could not be read', () => {
    const verdict = creditVerdict({ ...exam, before: null, after: snap(16) });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems).toEqual(['the balance before or after the run could not be read']);
  });
});

describe('live voice E2E: grading again is a no-op', () => {
  const retry = (over: Record<string, unknown> = {}) => ({ sessionId: 's1', status: 200, assessmentIdBefore: 'a1', assessmentIdAfter: 'a1', ...over });

  it('accepts 200 and 202 with the same assessment and an unchanged ledger', () => {
    expect(gradeRetryVerdict({ retries: [retry(), retry({ sessionId: 's2', status: 202, assessmentIdBefore: 'a2', assessmentIdAfter: 'a2' })], ledgerBefore: ['x'], ledgerAfter: ['x'] }))
      .toEqual({ ok: true, problems: [] });
  });

  it('fails a new assessment, another status, a missing assessment, a changed ledger and no retry at all', () => {
    expect(gradeRetryVerdict({ retries: [retry({ assessmentIdAfter: 'a9' })] }).problems).toEqual(['s1: the assessment changed (a1 to a9)']);
    expect(gradeRetryVerdict({ retries: [retry({ status: 429 })] }).problems).toEqual(['s1: the retry answered 429']);
    expect(gradeRetryVerdict({ retries: [retry({ assessmentIdBefore: null, assessmentIdAfter: null })] }).problems).toEqual(['s1: no assessment existed before the retry']);
    expect(gradeRetryVerdict({ retries: [retry()], ledgerBefore: ['x'], ledgerAfter: ['x', 'y'] }).problems).toEqual(['the credit ledger changed']);
    expect(gradeRetryVerdict({ retries: [] }).problems).toEqual(['no retry was made']);
  });

  it('does not judge the ledger when it was not read', () => {
    expect(gradeRetryVerdict({ retries: [retry()], ledgerBefore: null, ledgerAfter: ['x'] }).ok).toBe(true);
  });
});

describe('live voice E2E: History shows the run once, with its credits and score', () => {
  const run = Date.parse('2026-10-01T10:00:00Z');
  const examRow = {
    attemptId: 'spx_1', subtest: 'speaking', title: 'Full Speaking Mock', contentRef: 'spx_1',
    startedAt: '2026-10-01T10:01:00+00:00', submittedAt: '2026-10-01T10:12:00+00:00', status: 'completed',
    balanceSource: 'speaking', creditsUsed: 4, route: '/speaking/exam/spx_1/results', resultLabel: '192/500',
  };
  const older = { attemptId: 'att_old', subtest: 'speaking', title: 'Old card', startedAt: '2026-09-30T10:00:00+00:00', status: 'completed', creditsUsed: 0, route: '/speaking' };
  const exam = (over: Record<string, unknown> = {}, extra: Record<string, unknown> = {}) => historyVerdict({
    kind: 'exam', examId: 'spx_1', attempts: [{ ...examRow, ...over }, older], submissions: [], runStartMs: run,
    expectedCredits: 4, expectedScore: 192, pageText: 'Attempt activity\nFull Speaking Mock · 192/500', ...extra,
  });

  it('accepts one exam row with the title, route, status, 4 credits and the combined score', () => {
    const verdict = exam();
    expect(verdict.problems).toEqual([]);
    expect(verdict.ok).toBe(true);
    expect(verdict.row?.attemptId).toBe('spx_1');
  });

  it('fails a mock listed as one row per card (the old behaviour)', () => {
    const child = (id: string) => ({ attemptId: id, subtest: 'speaking', title: 'Card', startedAt: '2026-10-01T10:02:00+00:00', status: 'completed', creditsUsed: 0, route: '/speaking' });
    const verdict = historyVerdict({ kind: 'exam', examId: 'spx_1', attempts: [child('att_a'), child('att_b')], submissions: [], runStartMs: run, expectedCredits: 4 });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems.join('\n')).toContain('the run is listed 0 times, expected once');
    expect(verdict.problems.join('\n')).toContain('2 Speaking rows were started in this run');
  });

  it('fails the wrong title, route, status, credits or result label', () => {
    expect(exam({ title: 'Card A' }).problems.join('\n')).toContain('the row is titled "Card A"');
    expect(exam({ route: '/speaking' }).problems.join('\n')).toContain("the row's route is /speaking");
    expect(exam({ status: 'in_progress' }).problems.join('\n')).toContain("the row's status is in_progress");
    expect(exam({ creditsUsed: 0 }).problems.join('\n')).toContain('the row shows 0 credits used, expected 4');
    expect(exam({ resultLabel: null }).problems.join('\n')).toContain("the row's result label is null");
    expect(exam({ resultLabel: 'Marking in progress' }).problems.join('\n')).toContain('expected a score such as 192/500');
  });

  it('fails a label that is not the score the results page showed, and a History page that does not show it', () => {
    expect(exam({}, { expectedScore: 200 }).problems.join('\n')).toContain('the row says 192/500 but the results page showed 200/500');
    expect(exam({}, { pageText: 'Attempt activity\nFull Speaking Mock' }).problems.join('\n')).toContain('the History page does not show');
  });

  it('fails when Past evidence lists a Speaking attempt of this run, but not an old one or another subtest', () => {
    const listed = (items: unknown[]) => exam({}, { submissions: items }).problems.join('\n');
    expect(listed([{ subtest: 'speaking', attemptDate: '2026-10-01T10:05:00Z' }])).toContain('Past evidence lists 1 Speaking attempt(s) of this run');
    expect(listed([{ subtest: 'speaking', attemptDate: '2026-01-01T00:00:00Z' }, { subtest: 'writing', attemptDate: '2026-10-01T10:05:00Z' }])).toBe('');
  });

  it('does not judge Past evidence when it was not captured', () => {
    expect(exam({}, { submissions: null }).ok).toBe(true);
  });

  it('accepts a practice card: the row whose route is its results page, 2 credits and the score', () => {
    const row = {
      attemptId: 'att_9', subtest: 'speaking', title: 'Lactose card', startedAt: '2026-10-01T10:03:00+00:00', status: 'completed',
      creditsUsed: 2, route: '/speaking/sessions/sps_9/results', resultLabel: '308/500',
    };
    const verdict = historyVerdict({ kind: 'practice', sessionId: 'sps_9', attempts: [row, older], submissions: [], runStartMs: run, expectedCredits: 2, expectedScore: 308 });
    expect(verdict.problems).toEqual([]);
    expect(verdict.ok).toBe(true);
    expect(historyVerdict({ kind: 'practice', sessionId: 'sps_9', attempts: [{ ...row, route: '/speaking/sessions/sps_9' }], submissions: [], runStartMs: run, expectedCredits: 2 }).ok).toBe(false);
  });
});

describe('live voice E2E: results page wording', () => {
  const note = 'No audio recording is stored for live conversations, so there is nothing to play back. This transcript is what was marked.';
  const practice = {
    id: 'sps_1',
    banner: true,
    overview: 'Submission received\nWe saved the transcript of your live conversation on 1 Oct 2026 and queued it for marking.\n308 / 500',
    transcriptTab: `${note}\n[00:01] candidate Good morning`,
  };

  it('accepts a live practice page: banner, note, no recording word, no audio player', () => {
    expect(resultsWordingVerdict({ sessions: [practice] })).toEqual({ ok: true, problems: [] });
  });

  it('accepts an exam card page that has no banner when the transcript note says it was a live conversation', () => {
    expect(resultsWordingVerdict({ sessions: [{ id: 'sps_2', banner: false, overview: 'Card result 205 / 500', transcriptTab: note }] }).ok).toBe(true);
  });

  it('fails the old banner: it says recording, and does not say the transcript was saved', () => {
    const verdict = resultsWordingVerdict({ sessions: [{ ...practice, overview: 'We received your recording on 1 Oct 2026 and queued it for marking.' }] });
    expect(verdict.ok).toBe(false);
    expect(verdict.problems).toHaveLength(2);
    expect(verdict.problems[0]).toContain('the results page says "We received your recording on 1 Oct 2026');
    expect(verdict.problems[1]).toContain('the submission banner does not say the transcript of the live conversation was saved');
  });

  it('fails a transcript tab that still shows the dead audio player', () => {
    const verdict = resultsWordingVerdict({ sessions: [{ ...practice, transcriptTab: `Recording\nRecording unavailable\n${note}` }] });
    expect(verdict.problems).toEqual(['sps_1: the transcript tab still shows an audio player']);
  });

  it('fails a page that never says it was a live conversation', () => {
    const verdict = resultsWordingVerdict({ sessions: [{ id: 'sps_3', banner: false, overview: 'Card result', transcriptTab: 'candidate hello' }] });
    expect(verdict.problems).toEqual(['sps_3: nothing on the page says this was a live conversation']);
  });

  it('accepts exam results with a readable band and the advisory sentence', () => {
    const text = 'Combined result\n192 / 500\nReadiness band: Developing\nAI practice estimate, not an official OET result.\nView details and transcript';
    expect(resultsWordingVerdict({ exam: text })).toEqual({ ok: true, problems: [] });
  });

  it('fails exam results that show a raw band code, miss the advisory sentence or mention a recording', () => {
    const problems = (text: string) => resultsWordingVerdict({ exam: text }).problems.join('\n');
    expect(problems('Readiness band exam_ready\nNot an official OET result.')).toContain('the raw band code exam_ready is shown');
    expect(problems('Readiness band: Developing')).toContain('no advisory sentence');
    expect(problems('Your recordings are saved.\nnot an official OET score')).toContain('exam results: the page says "Your recordings are saved."');
  });

  it('does not take ordinary prose for a raw band code', () => {
    expect(resultsWordingVerdict({ exam: 'A strong opening and a developing sense of structure.\nnot an official OET result' }).ok).toBe(true);
  });

  it('finds raw band codes wherever they are printed as codes', () => {
    expect(rawBandCodes('Band EXAM_READY')).toEqual(['exam_ready']);
    expect(rawBandCodes('Readiness band\nstrong\nGood work')).toEqual(['strong']);
    expect(rawBandCodes('Readiness band developing')).toEqual(['developing']);
    expect(rawBandCodes('Readiness band: Developing\nExam-ready\nStrong')).toEqual([]);
  });

  it('lists only the lines that mention a recording', () => {
    expect(recordingMentions('Hello\nYour recording is saved\nBye')).toEqual(['Your recording is saved']);
    expect(recordingMentions('We saved the transcript and queued it for marking.')).toEqual([]);
  });
});

describe('live voice E2E: nearest-rank percentile', () => {
  const ascending = (n: number) => Array.from({ length: n }, (_, i) => (i + 1) * 100);

  it('reads the ceil(p n)-th value of an ascending list', () => {
    expect(nearestRank(ascending(20), 0.95)).toBe(1900);
    expect(nearestRank(ascending(10), 0.95)).toBe(1000);
    expect(nearestRank(ascending(104), 0.95)).toBe(9900);
    expect(nearestRank(ascending(4), 0.5)).toBe(200);
  });

  it('survives a list of one and an empty list', () => {
    expect(nearestRank([1500], 0.95)).toBe(1500);
    expect(nearestRank([], 0.95)).toBeNull();
  });
});
