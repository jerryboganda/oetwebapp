'use client';

import { pronunciationScoreTier } from '@/lib/scoring';

type WordScore = {
  word: string;
  accuracyScore: number;
  errorType: string;
};

export function PhonemeHeatmap({ wordScores }: { wordScores: WordScore[] }) {
  if (wordScores.length === 0) {
    return <p className="text-sm text-muted">No word-level data was returned for this attempt.</p>;
  }
  return (
    <div className="flex flex-wrap gap-2" role="list" aria-label="Per-word accuracy scores">
      {wordScores.map((ws, i) => (
        <div
          key={`${ws.word}-${i}`}
          role="listitem"
          title={`${ws.word}: ${Math.round(ws.accuracyScore)}% · ${friendlyError(ws.errorType)}`}
          className={`rounded-full px-3 py-1 text-sm font-medium border ${bucketClass(ws.accuracyScore)}`}
        >
          <span>{ws.word}</span>{' '}
          <span className="ml-1 font-mono text-[11px] opacity-80">
            {Math.round(ws.accuracyScore)}
          </span>
        </div>
      ))}
    </div>
  );
}

// Advisory per-word colour bucketing. Numeric thresholds (85 / 70) live in
// `pronunciationScoreTier` (`lib/scoring.ts`) so the 70 anchor is centralised.
function bucketClass(score: number) {
  switch (pronunciationScoreTier(score)) {
    case 'excellent':
      return 'bg-emerald-50 text-emerald-800 border-emerald-200 dark:bg-emerald-900/30 dark:text-emerald-200 dark:border-emerald-800';
    case 'passing':
      return 'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800';
    case 'below':
      return 'bg-rose-50 text-rose-800 border-rose-200 dark:bg-rose-900/30 dark:text-rose-200 dark:border-rose-800';
    default:
      return 'bg-background-light text-muted border-border dark:bg-gray-800 dark:text-muted/60 dark:border-gray-700';
  }
}

function friendlyError(errorType: string) {
  switch (errorType) {
    case 'None': return 'clear';
    case 'Mispronunciation': return 'mispronounced';
    case 'Omission': return 'missed';
    case 'Insertion': return 'extra';
    case 'NoData': return 'no data';
    default: return errorType;
  }
}
