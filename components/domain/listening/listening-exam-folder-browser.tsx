'use client';

import { useMemo, useState, type ReactNode } from 'react';
import { ChevronRight, Folder } from 'lucide-react';
import {
  groupListeningExamPapers,
  type ListeningExamCategoryId,
  type ListeningExamCategoryPaper,
} from '@/lib/listening-exam-categories';

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
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          Back to folders
        </button>
        <div>
          <h2 className="text-base font-bold text-navy">{openSection.title}</h2>
          <p className="mt-1 text-sm text-muted">{openSection.description}</p>
        </div>
        {openSection.papers.length === 0 ? (
          <p className="rounded-2xl border border-dashed border-border px-4 py-5 text-sm text-muted">
            {emptyMessage}
          </p>
        ) : (
          <ul className="grid gap-4 sm:grid-cols-2">
            {openSection.papers.map((paper) => (
              <li key={paper.id}>{renderPaper(paper)}</li>
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
      <ul className="overflow-hidden rounded-2xl border border-border bg-surface">
        {sections.map((section, index) => (
          <li key={section.id} className={index === 0 ? '' : 'border-t border-border'}>
            <button
              type="button"
              data-testid={`listening-exam-folder-${section.id}`}
              onClick={() => setOpenFolderId(section.id)}
              className="flex w-full items-center gap-3 px-4 py-3.5 text-left transition-colors hover:bg-muted/20"
            >
              <Folder className="h-5 w-5 shrink-0 text-muted" aria-hidden />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold text-navy">{section.title}</span>
                <span className="mt-0.5 block text-xs text-muted">
                  {section.papers.length === 0
                    ? 'No papers yet'
                    : `${section.papers.length} paper${section.papers.length === 1 ? '' : 's'}`}
                </span>
              </span>
              <ChevronRight className="h-4 w-4 shrink-0 text-muted" aria-hidden />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
