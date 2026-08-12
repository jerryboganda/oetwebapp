import { cn } from '@/lib/utils';

export interface ScoreBandGraphProps {
  rawScore: number;
  maxRawScore: number;
  scaledScore: number | null;
  passed?: boolean | null;
  grade?: string | null;
  tableVersion?: string | null;
  className?: string;
}
const PRACTICE_SCORE_LABEL = 'AI Practice Score — not an official OET result.';

/**
 * Platform-branded score-band visualization. It deliberately uses a compact
 * indigo/amber/cyan scale rather than the official OET result treatment. A
 * missing approved conversion stays visible as raw-only; no client formula
 * invents a scaled score.
 */
export function ScoreBandGraph({
  rawScore,
  maxRawScore,
  scaledScore,
  passed,
  grade,
  tableVersion,
  className,
}: ScoreBandGraphProps) {
  const hasConversion = maxRawScore === 42
    && scaledScore !== null
    && tableVersion != null
    && passed != null;
  const boundedScore = hasConversion
    ? Math.min(500, Math.max(0, scaledScore ?? 0))
    : 0;
  const rawPosition = maxRawScore > 0
    ? Math.min(100, Math.max(0, (rawScore / maxRawScore) * 100))
    : 0;
  const position = hasConversion ? `${boundedScore / 5}%` : `${rawPosition}%`;

  return (
    <section
      aria-label="Practice score band graph"
      className={cn('rounded-2xl border border-indigo-200/70 bg-indigo-50/60 p-4 dark:border-indigo-400/20 dark:bg-indigo-950/20', className)}
      data-testid="score-band-graph"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-black uppercase tracking-[0.16em] text-indigo-700 dark:text-indigo-300">Practice score band</p>
          <p className="mt-1 text-sm font-semibold text-navy dark:text-white">
            {hasConversion ? `${scaledScore}/500${grade ? ` · Grade ${grade}` : ''}` : `Scaled score unavailable · ${rawScore}/${maxRawScore} raw`}
          </p>
        </div>
        <p className="text-right text-xs font-semibold text-muted">
          {hasConversion ? `Table ${tableVersion}` : 'Owner-approved table required'}
        </p>
      </div>

      <div className="mt-5" role="img" aria-label={hasConversion ? `Practice scaled score ${scaledScore} out of 500` : `Raw practice score ${rawScore} out of ${maxRawScore}; scaled score unavailable`}>
        <div className="relative h-4 rounded-full bg-[linear-gradient(90deg,#4f46e5_0%,#06b6d4_62%,#f59e0b_70%,#f97316_100%)] shadow-inner">
          <span
            className="absolute -top-1.5 h-7 w-1 rounded-full bg-navy shadow-md dark:bg-white"
            style={{ left: `calc(${position} - 2px)` }}
            aria-hidden
          />
          <span
            className="absolute -top-7 -translate-x-1/2 rounded-md bg-navy px-2 py-1 text-[10px] font-black text-white dark:bg-white dark:text-navy"
            style={{ left: position }}
          >
            {hasConversion ? scaledScore : `${rawScore}/${maxRawScore}`}
          </span>
        </div>
        <div className="mt-2 flex justify-between text-[10px] font-black uppercase tracking-widest text-muted">
          <span>0</span>
          <span>{hasConversion ? '350 reference' : `Raw scale · ${maxRawScore}`}</span>
          <span>{hasConversion ? '500' : maxRawScore}</span>
        </div>
      </div>

      <p className="mt-4 border-t border-indigo-200/70 pt-3 text-xs font-semibold text-muted dark:border-indigo-400/20">
        {PRACTICE_SCORE_LABEL}
      </p>
    </section>
  );
}
