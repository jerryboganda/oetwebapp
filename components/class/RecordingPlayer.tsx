'use client';

import { useRef, useState } from 'react';
import { ChevronDown, ChevronUp, Clock, ListChecks, Sparkles } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';

type Chapter = { startSeconds: number; title: string; summary: string };

export type RecordingPlayerProps = {
  videoUrl: string;
  chapters: Chapter[];
  aiSummary?: string | null;
  aiSummaryAr?: string | null;
  actionItems: string[];
};

function formatSeconds(seconds: number) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

export function RecordingPlayer({
  videoUrl,
  chapters,
  aiSummary,
  aiSummaryAr,
  actionItems,
}: RecordingPlayerProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [summaryOpen, setSummaryOpen] = useState(true);
  const [summaryLang, setSummaryLang] = useState<'en' | 'ar'>('en');
  const [checkedItems, setCheckedItems] = useState<Set<number>>(new Set());

  const seekTo = (seconds: number) => {
    if (videoRef.current) {
      videoRef.current.currentTime = seconds;
      videoRef.current.play();
    }
  };

  const toggleCheck = (index: number) => {
    setCheckedItems((prev) => {
      const next = new Set(prev);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  };

  const displaySummary = summaryLang === 'ar' && aiSummaryAr ? aiSummaryAr : aiSummary;

  return (
    <div className="space-y-4">
      {/* Video player */}
      <div className="aspect-video w-full overflow-hidden rounded-xl bg-background-dark shadow-sm">
        <video
          ref={videoRef}
          src={videoUrl}
          preload="metadata"
          controls
          className="h-full w-full rounded-xl"
        />
      </div>

      {/* Chapter navigation */}
      {chapters.length > 0 && (
        <Card>
          <h3 className="flex items-center gap-2 text-sm font-semibold text-navy">
            <Clock className="h-4 w-4 text-primary" aria-hidden="true" />
            Chapters
          </h3>
          <ul className="mt-3 space-y-1">
            {chapters.map((chapter, i) => (
              <li key={i}>
                <button
                  type="button"
                  onClick={() => seekTo(chapter.startSeconds)}
                  className="group flex min-h-11 w-full items-start gap-3 rounded-lg px-3 py-2 text-start transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <span className="mt-0.5 min-w-12 rounded-md bg-primary/10 px-1.5 py-0.5 text-center font-mono text-xs font-semibold tabular-nums text-primary transition-colors group-hover:bg-primary/15">
                    {formatSeconds(chapter.startSeconds)}
                  </span>
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-navy">{chapter.title}</p>
                    {chapter.summary && (
                      <p className="mt-0.5 text-xs text-muted line-clamp-2">{chapter.summary}</p>
                    )}
                  </div>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {/* AI Summary */}
      {aiSummary && (
        <Card padding="none">
          {/* The language switch sits beside the toggle, not inside it: buttons cannot nest. */}
          <div className="flex items-center justify-between gap-2 px-4 py-2">
            <button
              type="button"
              onClick={() => setSummaryOpen((o) => !o)}
              aria-expanded={summaryOpen}
              className="flex min-h-11 items-center gap-2 rounded-lg text-start text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <Sparkles className="h-4 w-4 text-primary" aria-hidden="true" />
              AI Summary
              {summaryOpen ? (
                <ChevronUp className="h-4 w-4 text-muted" aria-hidden="true" />
              ) : (
                <ChevronDown className="h-4 w-4 text-muted" aria-hidden="true" />
              )}
            </button>
            {aiSummaryAr && (
              <span
                role="group"
                aria-label="Summary language"
                className="flex items-center rounded-full border border-border bg-background-light text-xs font-medium"
              >
                {(['en', 'ar'] as const).map((lang) => (
                  <button
                    key={lang}
                    type="button"
                    aria-pressed={summaryLang === lang}
                    onClick={() => setSummaryLang(lang)}
                    className={cn(
                      'min-h-11 px-3 transition-colors first:rounded-s-full last:rounded-e-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                      summaryLang === lang ? 'bg-primary text-white dark:bg-primary-700' : 'text-muted hover:text-navy',
                    )}
                  >
                    {lang.toUpperCase()}
                  </button>
                ))}
              </span>
            )}
          </div>
          {summaryOpen && (
            <div
              className="border-t border-border px-4 py-4 text-start text-sm leading-7 text-navy"
              dir={summaryLang === 'ar' ? 'rtl' : 'ltr'}
            >
              {displaySummary}
            </div>
          )}
        </Card>
      )}

      {/* Action Items */}
      {actionItems.length > 0 && (
        <Card>
          <h3 className="flex items-center gap-2 text-sm font-semibold text-navy">
            <ListChecks className="h-4 w-4 text-primary" aria-hidden="true" />
            Action Items
          </h3>
          <ul className="mt-3 space-y-1">
            {actionItems.map((item, i) => (
              <li key={i}>
                {/* The whole row is the 44px target; the item text is its accessible name. */}
                <button
                  type="button"
                  role="checkbox"
                  aria-checked={checkedItems.has(i)}
                  onClick={() => toggleCheck(i)}
                  className="group flex min-h-11 w-full items-start gap-3 rounded-lg px-1 py-1.5 text-start transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <span
                    aria-hidden="true"
                    className={cn(
                      'mt-1 flex h-4 w-4 shrink-0 items-center justify-center rounded border transition-colors',
                      checkedItems.has(i)
                        ? 'border-primary bg-primary text-white dark:bg-primary-700'
                        : 'border-border bg-background group-hover:border-primary/60',
                    )}
                  >
                    {checkedItems.has(i) && (
                      <svg
                        viewBox="0 0 10 8"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="1.5"
                        className="h-2.5 w-2.5"
                      >
                        <path d="M1 4l3 3 5-6" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                    )}
                  </span>
                  <span
                    className={cn(
                      'text-sm leading-6',
                      checkedItems.has(i) ? 'text-muted line-through' : 'text-navy',
                    )}
                  >
                    {item}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  );
}
