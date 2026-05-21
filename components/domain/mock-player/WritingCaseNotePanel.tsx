'use client';

import { FileText } from 'lucide-react';

interface WritingCaseNotePanelProps {
  html?: string;
  title?: string;
  compact?: boolean;
}

export function WritingCaseNotePanel({ html, title = 'Case notes', compact = false }: WritingCaseNotePanelProps) {
  return (
    <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-label={title}>
      <div className="mb-3 flex items-center gap-2">
        <FileText className="h-5 w-5 text-primary" aria-hidden />
        <div>
          <p className="text-sm font-black text-navy">{title}</p>
          <p className="text-xs text-muted">
            {compact ? 'Reference while writing. Do not copy irrelevant details.' : 'Read only during the first phase; the notes remain visible while writing.'}
          </p>
        </div>
      </div>
      {html ? (
        <div
          className={`prose prose-sm max-w-none text-navy ${compact ? 'max-h-72 overflow-auto' : ''}`}
          dangerouslySetInnerHTML={{ __html: html }}
        />
      ) : (
        <div className="rounded-xl border border-dashed border-border bg-background-light p-4 text-sm leading-6 text-muted">
          Case notes are not available in the local payload yet. The exam editor still enforces the 5+40 flow and will use the backend-provided notes when present.
        </div>
      )}
    </section>
  );
}
