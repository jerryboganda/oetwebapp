'use client';

/**
 * OverallVoiceNote — the single overall tutor voice note for a writing submission.
 *
 * Reuses the expert VoiceNoteRecorder UI but routes the upload at the
 * submission-keyed Writing V2 endpoint (System A) via the `uploader` prop instead
 * of the ReviewRequest-keyed criterion flow. Mounted in the marking workspace for
 * BOTH mock and normal writing — it is the tutor's voice feedback channel in both
 * contexts (per the Writing feedback rule).
 */

import { useEffect, useState } from 'react';
import { Mic } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { VoiceNoteRecorder } from '@/components/domain/expert/VoiceNoteRecorder';
import { fetchAuthorizedObjectUrl, uploadWritingMarkingVoiceNote } from '@/lib/api';
import type { WritingMarkingVoiceNoteDto } from '@/lib/writing/types';

export interface OverallVoiceNoteProps {
  submissionId: string;
  /** Existing note from the marking context, if the tutor already recorded one. */
  existingVoiceNote?: WritingMarkingVoiceNoteDto | null;
}

export function OverallVoiceNote({ submissionId, existingVoiceNote }: OverallVoiceNoteProps) {
  // The media endpoint is Bearer-authenticated, so an <audio src> cannot fetch it
  // directly — resolve an authorized object URL for playback of the existing note.
  const [existingUrl, setExistingUrl] = useState<string | null>(null);

  useEffect(() => {
    const path = existingVoiceNote?.url;
    if (!path) {
      setExistingUrl(null);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    fetchAuthorizedObjectUrl(path)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        objectUrl = url;
        setExistingUrl(url);
      })
      .catch(() => {
        if (!cancelled) setExistingUrl(null);
      });
    return () => {
      cancelled = true;
      if (objectUrl) {
        try { URL.revokeObjectURL(objectUrl); } catch { /* ignore */ }
      }
    };
  }, [existingVoiceNote?.url]);

  return (
    <Card padding="md">
      <h3 className="flex items-center gap-1.5 text-sm font-bold text-navy">
        <Mic className="h-4 w-4 text-primary" aria-hidden="true" /> Voice note feedback
      </h3>
      <p className="mt-0.5 text-xs text-muted">
        Record a short spoken summary for the learner. Re-recording replaces the previous note.
      </p>
      <div className="mt-3">
        <VoiceNoteRecorder
          reviewRequestId={submissionId}
          criterionCode="overall"
          subtest="writing"
          maxSeconds={600}
          uploader={uploadWritingMarkingVoiceNote}
          existingVoiceNoteUrl={existingUrl ?? undefined}
        />
      </div>
    </Card>
  );
}

export default OverallVoiceNote;
