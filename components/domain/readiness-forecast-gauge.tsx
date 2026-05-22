'use client';

interface ReadinessForecastGaugeProps {
  probability: number | null | undefined;
  confidenceBand?: 'Low' | 'Medium' | 'High' | string;
  targetDate?: string;
}

export function ReadinessForecastGauge({ probability, confidenceBand, targetDate }: ReadinessForecastGaugeProps) {
  const pct = probability == null ? null : Math.max(0, Math.min(100, probability));
  const needleAngle = pct == null ? null : Math.PI * (1 - pct / 100);
  const needleX = needleAngle == null ? null : 100 + 80 * Math.cos(needleAngle);
  const needleY = needleAngle == null ? null : 100 - 80 * Math.sin(needleAngle);
  const dash = pct == null ? 0 : 251 * (pct / 100);

  return (
    <div className="flex flex-col items-center text-center">
      <svg viewBox="0 0 200 120" className="w-56 h-auto">
        <defs>
          <linearGradient id="readinessGauge" x1="0%" y1="0%" x2="100%" y2="0%">
            <stop offset="0%" stopColor="var(--color-danger)" />
            <stop offset="50%" stopColor="var(--color-warning)" />
            <stop offset="100%" stopColor="var(--color-success)" />
          </linearGradient>
        </defs>
        <path d="M 20 100 A 80 80 0 0 1 180 100" fill="none" stroke="var(--color-border)" strokeWidth="14" strokeLinecap="round" />
        {pct == null ? null : (
          <>
            <path d="M 20 100 A 80 80 0 0 1 180 100" fill="none" stroke="url(#readinessGauge)" strokeWidth="14" strokeLinecap="round" strokeDasharray={`${dash} 251`} />
            <circle cx={needleX ?? 0} cy={needleY ?? 0} r="6" fill="var(--color-navy)" stroke="var(--color-surface)" strokeWidth="2" />
          </>
        )}
        <text x="100" y="92" textAnchor="middle" className="fill-navy" style={{ fontSize: 22, fontWeight: 700 }}>
          {pct == null ? 'Pending' : `${Math.round(pct)}%`}
        </text>
        <text x="100" y="112" textAnchor="middle" className="fill-muted" style={{ fontSize: 10, letterSpacing: 2, fontWeight: 700, textTransform: 'uppercase' }}>
          target-date probability
        </text>
      </svg>
      {targetDate && (
        <p className="text-xs text-muted mt-2">Chance of hitting target by <span className="font-bold text-navy">{targetDate}</span></p>
      )}
      {confidenceBand && (
        <p className="text-[11px] uppercase tracking-widest font-bold text-muted mt-2">Confidence: {confidenceBand}</p>
      )}
    </div>
  );
}
