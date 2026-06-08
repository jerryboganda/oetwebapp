'use client';

import type { WritingScenarioDto } from '@/lib/writing/types';
import { cn } from '@/lib/utils';
import { WritingStimulusViewer } from './WritingStimulusViewer';

export interface WritingStimulusProps {
  /** The writing scenario DTO, or null while loading. */
  scenario: WritingScenarioDto | null;
  /**
   * Reading-window lock. The PDF viewer is always hardened (copy/drag/print
   * blocked regardless); kept for API compatibility with the host pages.
   */
  locked?: boolean;
  /** Optional document title shown in the toolbar. */
  title?: string;
  /** Optional extra className passed through to whichever surface renders. */
  className?: string;
}

/**
 * WritingStimulus — chooser that routes to the real PDF viewer when a stimulus
 * PDF is available, or to a minimal prompt + fixed-instructions surface
 * otherwise.
 *
 * Selection logic:
 *   - `scenario.stimulusPdfDownloadPath` non-null → WritingStimulusViewer (PDF)
 *   - otherwise                                   → prompt + fixed instructions
 */
export function WritingStimulus({
  scenario,
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

  const prompt = scenario?.taskPromptMarkdown?.trim() || null;
  const instructions = (scenario?.fixedInstructions ?? []).filter((l) => l.trim());

  return (
    <div
      className={cn(
        'flex h-full flex-col overflow-hidden bg-navy/5',
        className,
      )}
    >
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-border bg-surface/90 px-3 py-2 backdrop-blur">
        <span className="truncate text-sm font-bold text-navy">{title ?? 'Writing task'}</span>
      </div>
      <div className="flex-1 overflow-auto overscroll-contain px-4 py-5">
        <article
          className="mx-auto rounded-sm border border-border bg-white px-8 py-9 text-navy shadow-[0_1px_3px_rgba(15,23,42,0.08),0_8px_24px_-12px_rgba(15,23,42,0.25)]"
          style={{ width: '100%', maxWidth: '640px' }}
        >
          {prompt ? (
            <section className="space-y-2">
              <h3 className="border-b border-border/70 pb-1 text-sm font-bold uppercase tracking-wide text-navy">
                Writing task
              </h3>
              <p className="whitespace-pre-line text-[15px] leading-7">{prompt}</p>
            </section>
          ) : (
            <p className="text-sm italic text-muted">No task prompt available.</p>
          )}

          {instructions.length > 0 ? (
            <ul className="mt-4 list-disc space-y-1 pl-5 text-[15px] leading-7 text-navy">
              {instructions.map((line, i) => (
                <li key={i}>{line}</li>
              ))}
            </ul>
          ) : null}
        </article>
      </div>
    </div>
  );
}
