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
          <span className="ml-1 font-mono text-2xs opacity-80">
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
      return 'bg-success/10 text-success-strong border-success/20';
    case 'passing':
      return 'bg-warning/10 text-warning-strong border-warning/20';
    case 'below':
      return 'bg-danger/10 text-danger-strong border-danger/20';
    default:
      return 'bg-background-light text-muted border-border dark:text-muted/60';
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
