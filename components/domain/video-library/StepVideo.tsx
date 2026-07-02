'use client';

/**
 * Video wizard — step 2: the video file itself.
 * Hosts the direct-to-Bunny TUS upload card. The step can advance once a
 * Bunny video exists (encoding may still be in flight — the review step's
 * hard gate covers "ready"). There is nothing to persist here beyond what
 * the card already saved, so no submit is registered (the shell's
 * navigate-with-save is null-safe).
 */

import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import type { AdminVideoDetail } from '@/lib/api/video-library';
import { BunnyVideoUploadCard } from './BunnyVideoUploadCard';

export function StepVideo() {
  const wizard = useAdminWizard<AdminVideoDetail>();
  const video = wizard.entity;

  const canAdvance = Boolean(video.bunnyVideoId);

  useStepRegistration('video', { canAdvance, submit: null });

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Video file</h2>
        <p className="text-sm text-muted">
          Upload the source file straight to Bunny Stream. Encoding runs in the background — you can
          continue with the other steps while it processes.
        </p>
      </header>

      <BunnyVideoUploadCard
        videoId={video.videoId}
        video={video}
        canWrite={wizard.canWrite}
        onChanged={() => void wizard.refresh()}
      />
    </div>
  );
}
