'use client';

/**
 * Video wizard — step 3 (optional): extras.
 * Thumbnail, caption tracks, chapters and PDF attachments. Thumbnail /
 * captions / attachments persist immediately through their sub-components;
 * the chapter list is owned here and saved via `PUT …/chapters` in the step
 * submit so it participates in navigate-with-save.
 */

import { useCallback, useState } from 'react';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  adminSetVideoChapters,
  type AdminVideoChapter,
  type AdminVideoDetail,
} from '@/lib/api/video-library';
import { ThumbnailPicker } from './ThumbnailPicker';
import { CaptionsManager } from './CaptionsManager';
import { ChaptersEditor } from './ChaptersEditor';
import { AttachmentsManager } from './AttachmentsManager';

export function StepExtras() {
  const wizard = useAdminWizard<AdminVideoDetail>();
  const video = wizard.entity;

  const [chapters, setChapters] = useState<AdminVideoChapter[]>(video.chapters ?? []);
  const [error, setError] = useState<string | null>(null);

  const submit = useCallback(async () => {
    const cleaned = chapters
      .filter((chapter) => chapter.title.trim().length > 0)
      .map((chapter) => ({ timeSeconds: chapter.timeSeconds, title: chapter.title.trim() }))
      .sort((a, b) => a.timeSeconds - b.timeSeconds);
    setError(null);
    try {
      await adminSetVideoChapters(video.videoId, cleaned);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the chapters.');
      throw e;
    }
    await wizard.refresh();
  }, [chapters, video.videoId, wizard]);

  // The whole step is optional — extras never block progression.
  useStepRegistration('extras', { canAdvance: true, submit });

  const refresh = () => void wizard.refresh();

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Extras</h2>
        <p className="text-sm text-muted">
          Optional polish: thumbnail, captions, chapters and downloadable PDFs. Everything here can be
          added later.
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <ThumbnailPicker video={video} canWrite={wizard.canWrite} onChanged={refresh} />
      <CaptionsManager video={video} canWrite={wizard.canWrite} onChanged={refresh} />
      <ChaptersEditor chapters={chapters} onChange={setChapters} disabled={!wizard.canWrite} />
      <AttachmentsManager video={video} canWrite={wizard.canWrite} onChanged={refresh} />
    </div>
  );
}
