import { Badge } from '@/components/ui/badge';
import { cardClassName } from '@/components/ui/card';
import { cn } from '@/lib/utils';

interface ScoreConversionEvidenceProps {
  assessment: 'Listening' | 'Reading';
  rawScore: number;
  maxRawScore: number;
  scaledScore: number | null;
  passed: boolean | null;
  grade?: string | null;
  tableVersion?: string | null;
  errorCode?: string | null;
  className?: string;
}

export function ScoreConversionEvidence({
  assessment,
  rawScore,
  maxRawScore,
  scaledScore,
  passed,
  grade,
  tableVersion,
  errorCode,
  className,
}: ScoreConversionEvidenceProps) {
  const rawPercent = maxRawScore > 0
    ? Math.min(100, Math.max(0, (rawScore / maxRawScore) * 100))
    : 0;
  const hasConversion = maxRawScore === 42
    && scaledScore != null
    && tableVersion != null
    && passed != null;
  const scaledPercent = !hasConversion || scaledScore == null
    ? null
    : Math.min(100, Math.max(0, (scaledScore / 500) * 100));
  const graphAriaLabel = hasConversion
    ? `${assessment} AI Practice Score, ${scaledScore} out of 500. Not an official OET result.`
    : `${assessment} AI Practice Score graph. Converted score unavailable. Not an official OET result.`;

  return (
    <section className={cn(cardClassName({}), className)} aria-label={`${assessment} score conversion evidence`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="eyebrow text-muted">Score evidence</p>
          <h2 className="mt-1 text-base font-bold text-navy">Owner-table mapping</h2>
        </div>
        <Badge variant="muted" size="md">
          {tableVersion ? `Version ${tableVersion}` : 'Version unavailable'}
        </Badge>
      </div>

      {hasConversion ? (
        <>
          <div className="mt-5" role="img" aria-label={`${rawScore} of ${maxRawScore} raw mapped to ${scaledScore} of 500 scaled`}>
            <div className="mb-2 flex items-center justify-between text-xs font-semibold tabular-nums text-muted">
              <span>Raw {rawScore}/{maxRawScore}</span>
              <span>Scaled {scaledScore}/500</span>
            </div>
            {/* The fill starts at the inline start, so the marker is placed from there too (RTL-safe). */}
            <div className="relative h-3 rounded-full bg-background-light" aria-hidden="true">
              <div className="h-3 rounded-full bg-primary/25" style={{ width: `${rawPercent}%` }} />
              <span
                className="absolute top-1/2 h-5 w-5 -translate-x-1/2 -translate-y-1/2 rounded-full border-4 border-surface bg-primary shadow-sm rtl:translate-x-1/2"
                style={{ insetInlineStart: `${rawPercent}%` }}
              />
            </div>
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            {grade ? <Badge size="md">Grade {grade}</Badge> : null}
            {passed != null ? (
              <Badge variant={passed ? 'success' : 'danger'} size="md">
                Owner table: {passed ? 'passed' : 'not passed'}
              </Badge>
            ) : null}
          </div>
        </>
      ) : (
        <p className="mt-4 text-sm leading-6 text-muted">
          The exact scaled result is unavailable until an approved owner conversion table is configured.
          {errorCode ? ` (${errorCode})` : ''}
        </p>
      )}
      <div
        className="mt-5 overflow-hidden rounded-2xl border border-navy/20 bg-navy p-4 text-white dark:bg-background-light"
        role="img"
        aria-label={graphAriaLabel}
      >
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="tile-label text-white/70">Platform score graph</p>
            <p className="mt-1 text-sm font-bold tracking-tight">AI Practice Score — not an official OET result</p>
          </div>
          <span className="rounded-full border border-white/20 bg-white/10 px-3 py-1 text-xs font-bold tabular-nums">
            {hasConversion ? `${scaledScore}/500` : 'Awaiting table'}
          </span>
        </div>
        <div className="relative mt-5 h-3 rounded-full bg-white/15" aria-hidden="true">
          <div
            className="h-3 rounded-full bg-primary"
            style={{ width: `${scaledPercent ?? 0}%` }}
          />
          {scaledPercent != null ? (
            <span
              className="absolute top-1/2 h-6 w-6 -translate-x-1/2 -translate-y-1/2 rounded-full border-4 border-navy bg-white shadow-sm rtl:translate-x-1/2 dark:border-background-light"
              style={{ insetInlineStart: `${scaledPercent}%` }}
            />
          ) : null}
        </div>
        <div className="mt-2 flex justify-between text-3xs font-bold tabular-nums text-white/60" aria-hidden="true">
          <span>0</span>
          <span>250</span>
          <span>500</span>
        </div>
      </div>
      <p className="mt-4 border-t border-border pt-3 text-xs leading-5 text-muted">
        Practice evidence only; this is not an official OET result.
      </p>
    </section>
  );
}
