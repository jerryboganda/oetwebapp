import { parseFault, recoveredAsRequested } from './live-voice-served-provider.mjs';

describe('live voice E2E: which fault a run asked for', () => {
  it('is off when both inputs are blank or unset, whatever else is set', () => {
    expect(parseFault({})).toEqual({ kind: null, atSeconds: null, stallIgnored: false });
    expect(parseFault({ dropAt: '', stallAt: '  ', pinnedProvider: 'gemini', failPrimary: true })).toEqual({ kind: null, atSeconds: null, stallIgnored: false });
  });

  it('reads a drop or a stall (the workflow passes strings)', () => {
    expect(parseFault({ dropAt: '40' })).toEqual({ kind: 'drop', atSeconds: 40, stallIgnored: false });
    expect(parseFault({ stallAt: ' 25.5 ' })).toEqual({ kind: 'stall', atSeconds: 25.5, stallIgnored: false });
  });

  it('lets DROP win when both are set and says the stall was ignored', () => {
    expect(parseFault({ dropAt: '30', stallAt: '50' })).toEqual({ kind: 'drop', atSeconds: 30, stallIgnored: true });
  });

  it.each(['abc', '0', '-5', 'Infinity', '1,5'])('rejects %s as a time', (bad) => {
    expect(() => parseFault({ dropAt: bad })).toThrow(/FAULT_DROP_AT_S must be a number of seconds above 0/);
    expect(() => parseFault({ stallAt: bad })).toThrow(/FAULT_STALL_AT_S must be a number of seconds above 0/);
  });

  it('rejects a pinned provider: it never recovers, so the fault would only kill the session', () => {
    expect(() => parseFault({ dropAt: '40', pinnedProvider: 'openai' })).toThrow(/blank VOICE_PROVIDER/);
    expect(() => parseFault({ stallAt: '40', pinnedProvider: 'gemini' })).toThrow(/blank VOICE_PROVIDER/);
  });

  it('rejects FAIL_PRIMARY: a recovery adds a create call its failover check does not expect', () => {
    expect(() => parseFault({ dropAt: '40', failPrimary: true })).toThrow(/FAIL_PRIMARY/);
  });

  it('rejects a fault that would fire after the conversation ended', () => {
    expect(parseFault({ dropAt: '109', maxAtSeconds: 110 }).kind).toBe('drop');
    expect(() => parseFault({ dropAt: '110', maxAtSeconds: 110 })).toThrow(/would never fire/);
    expect(() => parseFault({ stallAt: '300', maxAtSeconds: 280 })).toThrow(/FAULT_STALL_AT_S=300 is not before the end/);
  });
});

describe('live voice E2E: recoveredAsRequested', () => {
  const fired = { kind: 'drop', firedAt: 1_000, recoveredAt: 4_000 };
  const ok = { fault: fired, recoveries: 1, errorShown: false, patientAt: [900, 4_500] };

  it('is null when no fault was requested, even if the app recovered by itself', () => {
    expect(recoveredAsRequested({ ...ok, fault: { kind: null, firedAt: null, recoveredAt: null } })).toBeNull();
    expect(recoveredAsRequested({ ...ok, fault: undefined })).toBeNull();
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
