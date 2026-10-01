'use client';

import { useMemo, useState, type ReactNode } from 'react';
import { ChevronLeft, ChevronRight, Folder } from 'lucide-react';
import { cardClassName } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  groupListeningExamPapers,
  type ListeningExamCategoryId,
  type ListeningExamCategoryPaper,
} from '@/lib/listening-exam-categories';
import { cn } from '@/lib/utils';

export interface ListeningExamFolderItem extends ListeningExamCategoryPaper {
  id: string;
}

interface ListeningExamFolderBrowserProps<T extends ListeningExamFolderItem> {
  papers: T[];
  renderPaper: (paper: T) => ReactNode;
  emptyMessage: string;
  testId?: string;
}

/**
 * Listening-only series folders (Atlas Practice Series, Nova Practice Series).
 * Unmatched papers are dropped. New published Atlas/Nova papers land here
 * from slug, title, or tagsCsv.
 */
export function ListeningExamFolderBrowser<T extends ListeningExamFolderItem>({
  papers,
  renderPaper,
  emptyMessage,
  testId = 'listening-exam-folders',
}: ListeningExamFolderBrowserProps<T>) {
  const sections = useMemo(() => groupListeningExamPapers(papers), [papers]);
  const [openFolderId, setOpenFolderId] = useState<Exclude<ListeningExamCategoryId, 'other'> | null>(null);
  const openSection = sections.find((section) => section.id === openFolderId) ?? null;

  if (openSection) {
    return (
      <div className="space-y-4" data-testid={testId} data-open-folder={openSection.id}>
        <button
          type="button"
          onClick={() => setOpenFolderId(null)}
          className="-ms-1 inline-flex min-h-11 items-center gap-1.5 rounded-control px-1 text-sm font-semibold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden />
          Back to folders
        </button>
        <div>
          <h2 className="text-base font-bold text-navy">{openSection.title}</h2>
          <p className="mt-1 text-sm text-muted">{openSection.description}</p>
        </div>
        {openSection.papers.length === 0 ? (
          <EmptyState icon={<Folder className="h-8 w-8" aria-hidden />} title={emptyMessage} />
        ) : (
          <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {openSection.papers.map((paper, index) => (
              <li key={paper.id}>
                <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                  {renderPaper(paper)}
                </MotionItem>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-3" data-testid={testId}>
      <div>
        <h2 className="text-base font-bold text-navy">Choose a series</h2>
        <p className="mt-1 text-sm text-muted">
          Listening papers are grouped into Atlas Practice Series and Nova Practice Series. New published papers appear here automatically.
        </p>
      </div>
      <ul className={cn(cardClassName({ padding: 'none' }), 'divide-y divide-border overflow-hidden')}>
        {sections.map((section) => (
          <li key={section.id}>
            <button
              type="button"
              data-testid={`listening-exam-folder-${section.id}`}
              onClick={() => setOpenFolderId(section.id)}
              className="hover-primary flex min-h-14 w-full items-center gap-3 px-4 py-3.5 text-start transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
            >
              <Folder className="h-5 w-5 shrink-0 text-skill-listening" aria-hidden />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold text-navy">{section.title}</span>
                <span className="mt-0.5 block text-xs tabular-nums text-muted">
                  {section.papers.length === 0
                    ? 'No papers yet'
                    : `${section.papers.length} paper${section.papers.length === 1 ? '' : 's'}`}
                </span>
              </span>
              <ChevronRight className="h-4 w-4 shrink-0 text-muted rtl:rotate-180" aria-hidden />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
