'use client';

/**
 * TutorVoiceNotePlayer — plays the tutor's overall voice note for a writing
 * submission (mock + normal). Renders nothing when there is no submitted note or
 * when the id is not a writing submission (e.g. an evaluation id on the dual-
 * assessment page). The media endpoint is Bearer-authenticated, so the audio is
 * resolved into an authorized object URL rather than set directly on <audio src>.
 */

import { useEffect, useState } from 'react';
import { Mic } from 'lucide-react';
import { fetchAuthorizedObjectUrl, getWritingSubmissionVoiceNote } from '@/lib/api';

export interface TutorVoiceNotePlayerProps {
  submissionId: string;
  /** Override the default card wrapper styling. */
  className?: string;
}

export function TutorVoiceNotePlayer({ submissionId, className }: TutorVoiceNotePlayerProps) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    let objectUrl: string | null = null;
    void getWritingSubmissionVoiceNote(submissionId)
      .then((note) => {
        if (cancelled || !note?.url) return null;
        return fetchAuthorizedObjectUrl(note.url);
      })
      .then((resolved) => {
        if (!resolved) return;
        if (cancelled) {
          URL.revokeObjectURL(resolved);
          return;
        }
        objectUrl = resolved;
        setUrl(resolved);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });
    return () => {
      cancelled = true;
      if (objectUrl) {
        try { URL.revokeObjectURL(objectUrl); } catch { /* ignore */ }
      }
    };
  }, [submissionId]);

  if (!url) return null;

  return (
    <section
      aria-labelledby="tutor-voice-note-heading"
      className={className ?? 'rounded-2xl border border-border bg-surface p-5 shadow-sm'}
    >
      <h2 id="tutor-voice-note-heading" className="flex items-center gap-1.5 text-lg font-bold text-navy">
        <Mic className="h-5 w-5 text-primary" aria-hidden="true" /> Tutor voice feedback
      </h2>
      <audio controls src={url} preload="metadata" className="mt-3 w-full" aria-label="Tutor voice feedback" />
    </section>
  );
}

export default TutorVoiceNotePlayer;
