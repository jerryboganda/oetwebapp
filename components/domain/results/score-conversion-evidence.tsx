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
  const hasConversion = scaledScore != null && tableVersion != null;

  return (
    <section className={`rounded-2xl border border-border bg-surface p-5 shadow-sm ${className ?? ''}`} aria-label={`${assessment} score conversion evidence`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs font-black uppercase tracking-widest text-muted">Score evidence</p>
          <h2 className="mt-1 text-base font-black text-navy">Owner-table mapping</h2>
        </div>
        <span className="rounded-full border border-border bg-background-light px-3 py-1 text-xs font-bold text-muted">
          {tableVersion ? `Version ${tableVersion}` : 'Version unavailable'}
        </span>
      </div>

      {hasConversion ? (
        <>
          <div className="mt-5" role="img" aria-label={`${rawScore} of ${maxRawScore} raw mapped to ${scaledScore} of 500 scaled`}>
            <div className="mb-2 flex items-center justify-between text-xs font-semibold text-muted">
              <span>Raw {rawScore}/{maxRawScore}</span>
              <span>Scaled {scaledScore}/500</span>
            </div>
            <div className="relative h-3 rounded-full bg-background-light" aria-hidden="true">
              <div className="h-3 rounded-full bg-primary/25" style={{ width: `${rawPercent}%` }} />
              <span
                className="absolute top-1/2 h-5 w-5 -translate-x-1/2 -translate-y-1/2 rounded-full border-4 border-surface bg-primary shadow-sm"
                style={{ left: `${rawPercent}%` }}
              />
            </div>
          </div>
          <div className="mt-4 flex flex-wrap gap-2 text-xs font-bold">
            {grade ? <span className="rounded-full bg-primary/10 px-3 py-1 text-primary">Grade {grade}</span> : null}
            {passed != null ? (
              <span className={`rounded-full px-3 py-1 ${passed ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'}`}>
                Owner table: {passed ? 'passed' : 'not passed'}
              </span>
            ) : null}
          </div>
        </>
      ) : (
        <p className="mt-4 text-sm leading-6 text-muted">
          The exact scaled result is unavailable until an approved owner conversion table is configured.
          {errorCode ? ` (${errorCode})` : ''}
        </p>
      )}
      <p className="mt-4 border-t border-border pt-3 text-xs leading-5 text-muted">
        Practice evidence only; this is not an official OET result.
      </p>
    </section>
  );
}
