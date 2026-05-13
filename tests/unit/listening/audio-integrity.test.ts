import { resolveBlockedSeekTarget, shouldResumeAfterBlockedPause } from '@/lib/listening/audio-integrity';

describe('Listening audio integrity helpers', () => {
  it('allows practice-mode scrubbing and programmatic cue seeks', () => {
    expect(resolveBlockedSeekTarget({
      canScrub: true,
      requestedTime: 60,
      lastKnownTime: 10,
      allowedProgrammaticTarget: null,
    })).toBeNull();

    expect(resolveBlockedSeekTarget({
      canScrub: false,
      requestedTime: 60,
      lastKnownTime: 10,
      allowedProgrammaticTarget: 60,
    })).toBeNull();
  });

  it('snaps every non-programmatic strict-mode seek to the last known playhead', () => {
    expect(resolveBlockedSeekTarget({
      canScrub: false,
      requestedTime: 4,
      lastKnownTime: 12,
      allowedProgrammaticTarget: null,
    })).toBe(12);

    expect(resolveBlockedSeekTarget({
      canScrub: false,
      requestedTime: 55,
      lastKnownTime: 12,
      allowedProgrammaticTarget: null,
    })).toBe(12);

    expect(resolveBlockedSeekTarget({
      canScrub: false,
      requestedTime: 12.25,
      lastKnownTime: 12,
      allowedProgrammaticTarget: null,
    })).toBe(12);
  });

  it('restarts audio after unauthorized strict-mode pause only during audio playback', () => {
    expect(shouldResumeAfterBlockedPause({
      canPause: false,
      phase: 'audio',
      hasStarted: true,
      hasReachedEnd: false,
      allowedProgrammaticPause: false,
    })).toBe(true);

    expect(shouldResumeAfterBlockedPause({
      canPause: false,
      phase: 'review',
      hasStarted: true,
      hasReachedEnd: false,
      allowedProgrammaticPause: false,
    })).toBe(false);

    expect(shouldResumeAfterBlockedPause({
      canPause: false,
      phase: 'audio',
      hasStarted: true,
      hasReachedEnd: false,
      allowedProgrammaticPause: true,
    })).toBe(false);
  });
});