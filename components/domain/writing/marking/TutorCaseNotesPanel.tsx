'use client';

import { useEffect, useState } from 'react';
import { Highlighter } from 'lucide-react';
import { getTutorCaseNotes } from '@/lib/writing/exam-api';
import { parseHighlights } from '@/lib/writing/highlights';
import { WritingStimulusViewer } from '@/components/domain/writing/WritingStimulusViewer';
import type { WritingCaseNotesDto } from '@/lib/writing/types';

/**
 * Read-only Case Notes PDF showing the learner's own highlights, for the tutor
 * marking surface. Renders nothing when the scenario has no stimulus PDF (or the
 * fetch fails) so it never blocks the marking workspace.
 */
export function TutorCaseNotesPanel({ submissionId }: { submissionId: string }) {
  const [caseNotes, setCaseNotes] = useState<WritingCaseNotesDto | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    void getTutorCaseNotes(submissionId)
      .then((c) => {
        if (!cancelled) setCaseNotes(c);
      })
      .catch(() => {
        if (!cancelled) setCaseNotes(null);
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId]);

  if (!caseNotes?.stimulusPdfDownloadPath) return null;

  return (
    <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-label="Learner's highlighted case notes">
      <h2 className="flex items-center gap-1.5 text-sm font-bold text-navy">
        <Highlighter className="h-4 w-4 text-amber-600" aria-hidden="true" /> Learner&rsquo;s highlighted case notes
      </h2>
      <p className="mt-0.5 text-xs text-muted">The portions the learner highlighted during the exam (read-only).</p>
      <div className="mt-3 h-[60vh] overflow-hidden rounded-xl border border-border">
        <WritingStimulusViewer
          downloadPath={caseNotes.stimulusPdfDownloadPath}
          title="Case Notes"
          allowHighlight={false}
          highlights={parseHighlights(caseNotes.caseNoteHighlightsJson)}
        />
      </div>
    </section>
  );
}
