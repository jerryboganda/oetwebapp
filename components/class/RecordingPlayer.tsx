'use client';

import { useRef, useState } from 'react';
import { ChevronDown, ChevronUp, Clock, ListChecks, Sparkles } from 'lucide-react';

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
      <div className="aspect-video w-full overflow-hidden rounded-xl bg-black shadow-sm">
        <video
          ref={videoRef}
          src={videoUrl}
          controls
          className="h-full w-full rounded-xl"
        />
      </div>

      {/* Chapter navigation */}
      {chapters.length > 0 && (
        <div className="rounded-xl border border-border bg-surface p-4 shadow-sm">
          <h3 className="flex items-center gap-2 text-sm font-semibold text-navy">
            <Clock className="h-4 w-4 text-primary" />
            Chapters
          </h3>
          <ul className="mt-3 space-y-1">
            {chapters.map((chapter, i) => (
              <li key={i}>
                <button
                  type="button"
                  onClick={() => seekTo(chapter.startSeconds)}
                  className="group flex w-full items-start gap-3 rounded-lg px-3 py-2 text-left transition-colors hover:bg-background-light"
                >
                  <span className="mt-0.5 min-w-[3rem] rounded-md bg-primary/10 px-1.5 py-0.5 text-center text-xs font-mono font-semibold text-primary group-hover:bg-primary group-hover:text-white">
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
        </div>
      )}

      {/* AI Summary */}
      {aiSummary && (
        <div className="rounded-xl border border-border bg-surface shadow-sm">
          <button
            type="button"
            onClick={() => setSummaryOpen((o) => !o)}
            className="flex w-full items-center justify-between px-4 py-3 text-left"
          >
            <span className="flex items-center gap-2 text-sm font-semibold text-navy">
              <Sparkles className="h-4 w-4 text-primary" />
              AI Summary
            </span>
            <div className="flex items-center gap-2">
              {aiSummaryAr && (
                <span
                  role="group"
                  className="flex items-center rounded-full border border-border bg-background text-xs font-medium"
                  onClick={(e) => e.stopPropagation()}
                >
                  <button
                    type="button"
                    onClick={() => setSummaryLang('en')}
                    className={`rounded-l-full px-2.5 py-1 transition-colors ${
                      summaryLang === 'en'
                        ? 'bg-primary text-white'
                        : 'text-muted hover:text-navy'
                    }`}
                  >
                    EN
                  </button>
                  <button
                    type="button"
                    onClick={() => setSummaryLang('ar')}
                    className={`rounded-r-full px-2.5 py-1 transition-colors ${
                      summaryLang === 'ar'
                        ? 'bg-primary text-white'
                        : 'text-muted hover:text-navy'
                    }`}
                  >
                    AR
                  </button>
                </span>
              )}
              {summaryOpen ? (
                <ChevronUp className="h-4 w-4 text-muted" />
              ) : (
                <ChevronDown className="h-4 w-4 text-muted" />
              )}
            </div>
          </button>
          {summaryOpen && (
            <div
              className={`border-t border-border px-4 py-4 text-sm leading-7 text-navy ${
                summaryLang === 'ar' ? 'text-right' : ''
              }`}
              dir={summaryLang === 'ar' ? 'rtl' : 'ltr'}
            >
              {displaySummary}
            </div>
          )}
        </div>
      )}

      {/* Action Items */}
      {actionItems.length > 0 && (
        <div className="rounded-xl border border-border bg-surface p-4 shadow-sm">
          <h3 className="flex items-center gap-2 text-sm font-semibold text-navy">
            <ListChecks className="h-4 w-4 text-primary" />
            Action Items
          </h3>
          <ul className="mt-3 space-y-2">
            {actionItems.map((item, i) => (
              <li key={i} className="flex items-start gap-3">
                <button
                  type="button"
                  onClick={() => toggleCheck(i)}
                  aria-label={checkedItems.has(i) ? 'Mark incomplete' : 'Mark complete'}
                  className={`mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded border transition-colors ${
                    checkedItems.has(i)
                      ? 'border-primary bg-primary text-white'
                      : 'border-border bg-background hover:border-primary/60'
                  }`}
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
                </button>
                <span
                  className={`text-sm leading-6 ${
                    checkedItems.has(i) ? 'text-muted line-through' : 'text-navy'
                  }`}
                >
                  {item}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
