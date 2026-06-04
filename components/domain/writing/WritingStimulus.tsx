'use client';

import type { WritingScenarioDto } from '@/lib/writing/types';
import { WritingStimulusViewer } from './WritingStimulusViewer';
import { CaseNotePdfViewer } from './CaseNotePdfViewer';

export interface WritingStimulusProps {
  /** The writing scenario DTO, or null while loading. */
  scenario: WritingScenarioDto | null;
  /**
   * Reading-window lock. The PDF viewer is always hardened (copy/drag/print
   * blocked regardless), so this only gates the text-fallback's selection lock.
   */
  locked?: boolean;
  /** Optional document title shown in the toolbar. */
  title?: string;
  /** Optional extra className passed through to whichever surface renders. */
  className?: string;
}

/**
 * WritingStimulus — chooser that routes to the real PDF viewer when a stimulus
 * PDF is available, or to the text-based CaseNotePdfViewer as a fallback.
 *
 * Selection logic:
 *   - `scenario.stimulusPdfDownloadPath` non-null  → WritingStimulusViewer
 *   - otherwise                                    → CaseNotePdfViewer (text)
 */
export function WritingStimulus({
  scenario,
  locked,
  title,
  className,
}: WritingStimulusProps) {
  const path = scenario?.stimulusPdfDownloadPath ?? null;

  if (path) {
    return (
      <WritingStimulusViewer
        downloadPath={path}
        title={title}
        className={className}
      />
    );
  }

  // TEXT FALLBACK — remove this branch once every writing task has a stimulus PDF.
  return (
    <CaseNotePdfViewer
      caseNotesMarkdown={scenario?.caseNotesMarkdown}
      caseNoteSections={scenario?.caseNoteSections}
      recipient={scenario?.recipient ?? null}
      taskPrompt={scenario?.taskPromptMarkdown ?? null}
      title={title}
      readingWindowLocked={locked}
      className={className}
    />
  );
}
