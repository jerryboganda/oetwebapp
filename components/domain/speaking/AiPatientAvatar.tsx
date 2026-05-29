'use client';

import { useMemo } from 'react';

export interface AiPatientAvatarProps {
  patientName?: string | null;
  patientAge?: number | null;
  interlocutorRole?: string | null;
  /** When true, the avatar shows a subtle pulse animation under the face. */
  isSpeaking?: boolean;
  /** When true, learner is currently speaking (mirrors mic level). */
  learnerIsSpeaking?: boolean;
  className?: string;
}

/**
 * Minimal animated AI-patient avatar used inside the role-play room.
 * Pure presentational — no audio bindings. The parent passes `isSpeaking`
 * driven by TTS playback events from the SignalR turn loop.
 */
export function AiPatientAvatar({
  patientName,
  patientAge,
  interlocutorRole,
  isSpeaking = false,
  learnerIsSpeaking = false,
  className,
}: AiPatientAvatarProps) {
  const initials = useMemo(() => {
    if (!patientName) return '👤';
    const parts = patientName.trim().split(/\s+/);
    return parts.length === 1
      ? parts[0]!.slice(0, 2).toUpperCase()
      : `${parts[0]![0]}${parts.at(-1)![0]}`.toUpperCase();
  }, [patientName]);

  return (
    <div className={['flex flex-col items-center gap-2', className].filter(Boolean).join(' ')}>
      <div
        className={[
          'relative flex h-24 w-24 items-center justify-center rounded-full bg-primary text-2xl font-semibold text-primary-foreground shadow-sm ring-2 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200',
          isSpeaking ? 'ring-primary' : 'ring-transparent',
        ].join(' ')}
        aria-live="polite"
        aria-label={
          isSpeaking
            ? `${patientName ?? 'AI patient'} is speaking`
            : `${patientName ?? 'AI patient'}`
        }
      >
        <span aria-hidden>{initials}</span>
        {isSpeaking ? (
          <span className="absolute inset-0 motion-safe:animate-ping rounded-full bg-primary/20" aria-hidden />
        ) : null}
      </div>

      <div className="flex flex-col items-center gap-1 text-center text-sm">
        {patientName ? (
          <span className="font-medium text-foreground">{patientName}</span>
        ) : null}
        <div className="flex flex-wrap items-center justify-center gap-1">
          {typeof patientAge === 'number' ? (
            <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted">
              {patientAge}
            </span>
          ) : null}
          {interlocutorRole ? (
            <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted">
              {interlocutorRole}
            </span>
          ) : null}
        </div>
      </div>

      <div
        className={[
          'h-1 w-20 rounded-full bg-muted transition-opacity',
          learnerIsSpeaking ? 'opacity-100' : 'opacity-30',
        ].join(' ')}
        aria-hidden
      >
        <div
          className={[
            'h-full rounded-full bg-success transition-[width] duration-300',
            learnerIsSpeaking ? 'w-3/4 motion-safe:animate-pulse' : 'w-0',
          ].join(' ')}
        />
      </div>
    </div>
  );
}

export default AiPatientAvatar;
