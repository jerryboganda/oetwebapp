export type ListeningAudioPhase = 'preview' | 'audio' | 'review';

export interface SeekGuardInput {
  canScrub: boolean;
  requestedTime: number;
  lastKnownTime: number;
  allowedProgrammaticTarget: number | null;
  toleranceSeconds?: number;
}

export function resolveBlockedSeekTarget({
  canScrub,
  requestedTime,
  lastKnownTime,
  allowedProgrammaticTarget,
  toleranceSeconds = 0.75,
}: SeekGuardInput): number | null {
  if (canScrub) return null;
  if (
    allowedProgrammaticTarget !== null
    && Math.abs(requestedTime - allowedProgrammaticTarget) <= toleranceSeconds
  ) {
    return null;
  }
  return Math.max(0, lastKnownTime);
}

export interface PauseGuardInput {
  canPause: boolean;
  phase: ListeningAudioPhase;
  hasStarted: boolean;
  hasReachedEnd: boolean;
  allowedProgrammaticPause: boolean;
}

export function shouldResumeAfterBlockedPause({
  canPause,
  phase,
  hasStarted,
  hasReachedEnd,
  allowedProgrammaticPause,
}: PauseGuardInput): boolean {
  return !canPause
    && phase === 'audio'
    && hasStarted
    && !hasReachedEnd
    && !allowedProgrammaticPause;
}