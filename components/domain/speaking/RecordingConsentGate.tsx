'use client';

/**
 * Phase 7 (C.3) of the OET Speaking module plan.
 *
 * Convenience wrapper that other Phase 2/3 routes can drop in front of
 * their recorder. It tracks the consent state in component memory and
 * only mounts `children` once the learner accepts the
 * `SpeakingConsentBanner`. Keeps the consent collection out of the
 * recorder's responsibility so the existing 50KB recorder page
 * (`app/speaking/task/[id]/page.tsx`) does not need to ship a forked
 * version of the banner.
 *
 * Example usage:
 *
 *   <RecordingConsentGate sessionMode="ai">
 *     <SpeakingRecorder ... />
 *   </RecordingConsentGate>
 */

import { useState, type ReactNode } from 'react';
import {
  SpeakingConsentBanner,
  type SpeakingSessionMode,
} from '@/components/domain/speaking/SpeakingConsentBanner';

export interface RecordingConsentGateProps {
  sessionMode: SpeakingSessionMode;
  /** Optional explicit version (e.g. for testing or overriding the
   * server-configured current version). */
  consentVersionOverride?: string;
  children: ReactNode;
  /** When true, skip the banner entirely (caller has already proven
   * consent via the server-side `/v1/speaking/consents/me` history). */
  alreadyAccepted?: boolean;
}

export function RecordingConsentGate({
  sessionMode,
  consentVersionOverride,
  children,
  alreadyAccepted = false,
}: RecordingConsentGateProps) {
  const [accepted, setAccepted] = useState(alreadyAccepted);

  if (!accepted) {
    return (
      <SpeakingConsentBanner
        sessionMode={sessionMode}
        consentVersionOverride={consentVersionOverride}
        onAccepted={() => setAccepted(true)}
      />
    );
  }

  return <>{children}</>;
}

export default RecordingConsentGate;
