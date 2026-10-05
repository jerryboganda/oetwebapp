import { describe, expect, it } from 'vitest';
import {
  formatAdmissionWait,
  isTransientAdmissionFailure,
  isWaitingForAdmission,
  type SpeakingLiveAdmission,
} from '../speaking-admission';

const waiting: SpeakingLiveAdmission = {
  status: 'waiting',
  position: 1,
  queueLength: 1,
  estimatedWaitSeconds: 9,
  pollAfterSeconds: 4,
};

describe('speaking admission helpers', () => {
  it('treats only a waiting admission as waiting', () => {
    expect(isWaitingForAdmission(waiting)).toBe(true);
    expect(isWaitingForAdmission(null)).toBe(false);
    expect(isWaitingForAdmission(undefined)).toBe(false);
  });

  it('puts a wait estimate in plain words', () => {
    expect(formatAdmissionWait(0)).toBe('less than a minute');
    expect(formatAdmissionWait(59)).toBe('less than a minute');
    expect(formatAdmissionWait(60)).toBe('about 1 minute');
    expect(formatAdmissionWait(61)).toBe('about 2 minutes');
    expect(formatAdmissionWait(190)).toBe('about 4 minutes');
    expect(formatAdmissionWait(3600)).toBe('about 1 hour');
    expect(formatAdmissionWait(4800)).toBe('about 1 hour 20 minutes');
    expect(formatAdmissionWait(-5)).toBe('less than a minute');
  });

  it('repeats only the failures that waiting can cure', () => {
    expect(isTransientAdmissionFailure(undefined)).toBe(true);
    expect(isTransientAdmissionFailure(503)).toBe(true);
    expect(isTransientAdmissionFailure(502)).toBe(true);
    expect(isTransientAdmissionFailure(429)).toBe(true);
    expect(isTransientAdmissionFailure(408)).toBe(true);
    expect(isTransientAdmissionFailure(402)).toBe(false);
    expect(isTransientAdmissionFailure(404)).toBe(false);
    expect(isTransientAdmissionFailure(409)).toBe(false);
    expect(isTransientAdmissionFailure(403)).toBe(false);
  });
});
