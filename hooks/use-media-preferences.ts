'use client';

import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '@/contexts/auth-context';
import { fetchSettingsSection } from '@/lib/api';
import { queryKeys } from '@/lib/query/hooks';

/**
 * Learner media preferences (Settings → Audio Preferences).
 *
 * These are persisted via `PATCH /v1/settings/audio` and were, until this hook
 * existed, saved but never read — changing them did nothing. The hook reads the
 * same react-query cache the settings page writes to, so saving on the settings
 * page takes effect without a reload.
 *
 * Defaults preserve the app's current behaviour (1x speed, prompts shown, full
 * bandwidth), so an unset preference never changes anything.
 */
export interface LearnerMediaPreferences {
  /** Default audio playback rate (e.g. 1, 1.25). */
  playbackSpeed: number;
  /** Whether transcript-support hints are shown on review/practice surfaces. */
  showTranscriptPrompts: boolean;
  /** Reduce preloading / heavy media fetches on slow connections. */
  lowBandwidthMode: boolean;
}

const DEFAULTS: LearnerMediaPreferences = {
  playbackSpeed: 1,
  showTranscriptPrompts: true,
  lowBandwidthMode: false,
};

export function useLearnerMediaPreferences(): LearnerMediaPreferences {
  const { user, isAuthenticated, loading } = useAuth();
  const userId = user?.userId ?? 'current';

  const query = useQuery({
    queryKey: queryKeys.settings.section(userId, 'audio'),
    queryFn: () => fetchSettingsSection('audio'),
    enabled: isAuthenticated && !loading,
    staleTime: 60_000,
    retry: false,
  });

  return useMemo<LearnerMediaPreferences>(() => {
    const values = query.data?.values as Record<string, unknown> | undefined;
    if (!values) return DEFAULTS;
    const speed = Number(values.playbackSpeed);
    return {
      playbackSpeed: Number.isFinite(speed) && speed > 0 ? speed : DEFAULTS.playbackSpeed,
      // Unset → shown (current behaviour); only an explicit `false` hides prompts.
      showTranscriptPrompts:
        values.showTranscriptPrompts === undefined ? DEFAULTS.showTranscriptPrompts : Boolean(values.showTranscriptPrompts),
      lowBandwidthMode: Boolean(values.lowBandwidthMode),
    };
  }, [query.data]);
}

/** Convenience selector for components that only care about low-bandwidth mode. */
export function useLowBandwidthMode(): boolean {
  return useLearnerMediaPreferences().lowBandwidthMode;
}
