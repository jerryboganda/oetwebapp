import { createServedRecord, isNavigationAbortNoise } from './live-voice-served-provider.mjs';

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
