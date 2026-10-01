import { failoverCallsOk, parseFault, recoveredAsRequested } from './live-voice-served-provider.mjs';

describe('live voice E2E: which fault a run asked for', () => {
  const off = { kind: null, atSeconds: null, stallIgnored: false, reloadIgnored: false };

  it('is off when every input is blank or unset, whatever else is set', () => {
    expect(parseFault({})).toEqual(off);
    expect(parseFault({ dropAt: '', stallAt: '  ', reloadAt: '', pinnedProvider: 'gemini' })).toEqual(off);
  });

  it('reads a drop, a stall or a reload (the workflow passes strings)', () => {
    expect(parseFault({ dropAt: '40' })).toEqual({ kind: 'drop', atSeconds: 40, stallIgnored: false, reloadIgnored: false });
    expect(parseFault({ stallAt: ' 25.5 ' })).toEqual({ kind: 'stall', atSeconds: 25.5, stallIgnored: false, reloadIgnored: false });
    expect(parseFault({ reloadAt: '60' })).toEqual({ kind: 'reload', atSeconds: 60, stallIgnored: false, reloadIgnored: false });
  });

  it('lets DROP win over STALL over RELOAD and says which were ignored', () => {
    expect(parseFault({ dropAt: '30', stallAt: '50' })).toEqual({ kind: 'drop', atSeconds: 30, stallIgnored: true, reloadIgnored: false });
    expect(parseFault({ dropAt: '30', stallAt: '50', reloadAt: '70' })).toEqual({ kind: 'drop', atSeconds: 30, stallIgnored: true, reloadIgnored: true });
    expect(parseFault({ stallAt: '50', reloadAt: '70' })).toEqual({ kind: 'stall', atSeconds: 50, stallIgnored: false, reloadIgnored: true });
  });

  it.each(['abc', '0', '-5', 'Infinity', '1,5'])('rejects %s as a time', (bad) => {
    expect(() => parseFault({ dropAt: bad })).toThrow(/FAULT_DROP_AT_S must be a number of seconds above 0/);
    expect(() => parseFault({ stallAt: bad })).toThrow(/FAULT_STALL_AT_S must be a number of seconds above 0/);
    expect(() => parseFault({ reloadAt: bad })).toThrow(/FAULT_RELOAD_AT_S must be a number of seconds above 0/);
  });

  it('rejects a pinned provider for a drop or a stall: it never recovers, so the fault would only kill the session', () => {
    expect(() => parseFault({ dropAt: '40', pinnedProvider: 'openai' })).toThrow(/blank VOICE_PROVIDER/);
    expect(() => parseFault({ stallAt: '40', pinnedProvider: 'gemini' })).toThrow(/blank VOICE_PROVIDER/);
    // a drop that wins over a reload still needs a recovery
    expect(() => parseFault({ dropAt: '40', reloadAt: '60', pinnedProvider: 'gemini' })).toThrow(/blank VOICE_PROVIDER/);
  });

  it('allows a reload with a pinned provider: it needs no recovery', () => {
    expect(parseFault({ reloadAt: '60', pinnedProvider: 'gemini' })).toEqual({ kind: 'reload', atSeconds: 60, stallIgnored: false, reloadIgnored: false });
  });

  it('rejects a fault that would fire after the conversation ended', () => {
    expect(parseFault({ dropAt: '109', maxAtSeconds: 110 }).kind).toBe('drop');
    expect(() => parseFault({ dropAt: '110', maxAtSeconds: 110 })).toThrow(/would never fire/);
    expect(() => parseFault({ stallAt: '300', maxAtSeconds: 280 })).toThrow(/FAULT_STALL_AT_S=300 is not before the end/);
    expect(() => parseFault({ reloadAt: '280', maxAtSeconds: 280 })).toThrow(/FAULT_RELOAD_AT_S=280 is not before the end/);
  });
});

describe('live voice E2E: recoveredAsRequested', () => {
  const fired = { kind: 'drop', firedAt: 1_000, recoveredAt: 4_000 };
  const ok = { fault: fired, recoveries: 1, errorShown: false, patientAt: [900, 4_500] };

  it('is null when no fault was requested, even if the app recovered by itself', () => {
    expect(recoveredAsRequested({ ...ok, fault: { kind: null, firedAt: null, recoveredAt: null } })).toBeNull();
    expect(recoveredAsRequested({ ...ok, fault: undefined })).toBeNull();
  });

  it('is null for a page reload: it has no recovery to judge (metrics.reload carries its result)', () => {
    expect(recoveredAsRequested({ ...ok, fault: { kind: 'reload', firedAt: 1_000, recoveredAt: null } })).toBeNull();
  });

  it('is true when the panel reports a recovery and the patient spoke after the recovery session was asked for', () => {
    expect(recoveredAsRequested(ok)).toBe(true);
    expect(recoveredAsRequested({ ...ok, recoveries: 2 })).toBe(true);
  });

  it('is false when the fault never fired, whatever else looks fine', () => {
    expect(recoveredAsRequested({ ...ok, fault: { kind: 'stall', firedAt: null, recoveredAt: null } })).toBe(false);
  });

  it('is false when the panel reports no recovery (or was never read)', () => {
    expect(recoveredAsRequested({ ...ok, recoveries: 0 })).toBe(false);
    expect(recoveredAsRequested({ ...ok, recoveries: null })).toBe(false);
  });

  it('is false when the patient only spoke before the recovery session (the tail of the dead transport does not count)', () => {
    expect(recoveredAsRequested({ ...ok, patientAt: [900, 1_200, 3_999, 4_000] })).toBe(false);
    expect(recoveredAsRequested({ ...ok, patientAt: [] })).toBe(false);
  });

  it('is false when the run ended in the error state, and unknown error state does not block', () => {
    expect(recoveredAsRequested({ ...ok, errorShown: true })).toBe(false);
    expect(recoveredAsRequested({ ...ok, errorShown: null })).toBe(true);
  });

  it('falls back to the fault time when no recovery create call was seen', () => {
    const noCall = { ...ok, fault: { kind: 'stall', firedAt: 1_000, recoveredAt: null } };
    expect(recoveredAsRequested({ ...noCall, patientAt: [1_001] })).toBe(true);
    expect(recoveredAsRequested({ ...noCall, patientAt: [1_000] })).toBe(false);
  });
});

describe('live voice E2E: the provider create calls of a FAIL_PRIMARY run', () => {
  const P = 'openai/offer';
  const S = 'gemini/token';
  const want = (calls: string[], cards: number, extra: { recoveries?: number; reloads?: number } = {}) =>
    failoverCallsOk({ calls, primaryCall: P, secondaryCall: S, cards, ...extra });

  it('one practice card: the failed primary, then the secondary that serves', () => {
    expect(want([P, S], 1)).toBe(true);
  });

  it('an exam: that pair for each card', () => {
    expect(want([P, S, P, S], 2)).toBe(true);
  });

  it('a first recovery retries the provider that served (the secondary), not the failed one', () => {
    expect(want([P, S, S], 1, { recoveries: 1 })).toBe(true);
    expect(want([P, S, P, S], 1, { recoveries: 1 })).toBe(false);
  });

  it('an exam with a recovery on Card A: the extra call lands inside Card A, before Card B starts', () => {
    expect(want([P, S, S, P, S], 2, { recoveries: 1 })).toBe(true);
    expect(want([P, S, P, S, S], 2, { recoveries: 1 })).toBe(false);
  });

  it('a second recovery tries the other provider first (failed again) and then the secondary', () => {
    expect(want([P, S, S, P, S], 1, { recoveries: 2 })).toBe(true);
    expect(want([P, S, S, S], 1, { recoveries: 2 })).toBe(false);
  });

  it('a reload walks the candidates again: primary, secondary', () => {
    expect(want([P, S, P, S], 1, { reloads: 1 })).toBe(true);
    expect(want([P, S, S], 1, { reloads: 1 })).toBe(false);
  });

  it('is false for a run that made no failover, an extra primary call or the wrong order', () => {
    expect(want([S], 1)).toBe(false);
    expect(want([P, S, P], 1)).toBe(false);
    expect(want([S, P], 1)).toBe(false);
    expect(want([], 1)).toBe(false);
  });

  it('is false when a recovery was expected but did not happen', () => {
    expect(want([P, S], 1, { recoveries: 1 })).toBe(false);
  });
});
