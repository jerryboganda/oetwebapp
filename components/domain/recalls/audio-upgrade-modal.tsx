'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { Lock } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';

/**
 * Shared upgrade-prompt for paid-only Recalls audio.
 *
 * Per PRD Phase 2 §3, the listen-to-pronounce experience on Recalls is paid
 * only. The backend `/v1/recalls/audio/{termId}` endpoint returns 402
 * `subscription_required` when the requesting learner is on the free tier or
 * has a frozen subscription.
 *
 * `useRecallsAudioUpgrade` returns:
 *  - `guardAudio`: a wrapper that runs an audio fetch/play action and opens
 *    the upgrade modal if a 402/403 surfaces.
 *  - `modal`: the JSX to render once near the surface that owns the experience.
 */
export function useRecallsAudioUpgrade() {
  const [open, setOpen] = useState(false);

  const guardAudio = useCallback(
    async <T,>(action: () => Promise<T>, context?: { termId?: string }): Promise<T | null> => {
      try {
        return await action();
      } catch (err) {
        if (isApiError(err) && (err.status === 402 || err.status === 403)) {
          analytics.track('recalls_word_audio_blocked', context?.termId ? { termId: context.termId } : undefined);
          setOpen(true);
          return null;
        }
        throw err;
      }
    },
    [],
  );

  const modal = (
    <Modal
      open={open}
      onClose={() => setOpen(false)}
      title="Upgrade to hear pronunciations"
      size="sm"
    >
      <div className="space-y-4">
        <div className="flex items-center gap-3 rounded-xl bg-amber-50 p-3 text-amber-900">
          <Lock className="h-5 w-5 flex-shrink-0" />
          <p className="text-sm">
            British TTS pronunciations for Recalls cards are part of paid plans.
          </p>
        </div>
        <p className="text-sm text-muted">
          Upgrade to unlock listen-to-pronounce, listen-and-type drills, and the
          full active-recall queue.
        </p>
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={() => setOpen(false)}>
            Not now
          </Button>
          <Link href="/billing/upgrade" className="inline-flex">
            <Button variant="primary">Upgrade</Button>
          </Link>
        </div>
      </div>
    </Modal>
  );

  return { guardAudio, modal };
}
