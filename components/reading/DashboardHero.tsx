'use client';

interface DashboardHeroProps {
  readinessScore: number;
  predictedScore: number;
  daysToExam: number | null;
  streak: number;
}

interface StatCard {
  label: string;
  value: string;
  sub?: string;
  accent: string;
}

export function DashboardHero({ readinessScore, predictedScore, daysToExam, streak }: DashboardHeroProps) {
  const cards: StatCard[] = [
    {
      label: 'Readiness',
      value: `${readinessScore}%`,
      sub: readinessScore >= 80 ? 'Exam ready' : readinessScore >= 60 ? 'Getting there' : 'Keep going',
      accent: readinessScore >= 80 ? 'text-success' : readinessScore >= 60 ? 'text-warning' : 'text-danger',
    },
    {
      label: 'Predicted OET',
      value: predictedScore > 0 ? String(predictedScore) : '–',
      sub: predictedScore >= 350 ? 'Pass band' : predictedScore > 0 ? 'Below pass' : 'No data yet',
      accent: predictedScore >= 350 ? 'text-success' : 'text-muted',
    },
    {
      label: 'Days to Exam',
      value: daysToExam != null ? String(daysToExam) : '–',
      sub: daysToExam != null ? (daysToExam <= 14 ? 'Final sprint!' : `${Math.ceil(daysToExam / 7)} weeks`) : 'No exam set',
      accent: daysToExam != null && daysToExam <= 14 ? 'text-danger' : 'text-muted',
    },
    {
      label: 'Streak',
      value: streak > 0 ? `${streak} day${streak === 1 ? '' : 's'}` : '0 days',
      sub: streak >= 7 ? 'On fire!' : streak > 0 ? 'Keep it up' : 'Start today',
      accent: streak >= 7 ? 'text-orange-500 dark:text-orange-400' : 'text-muted',
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      {cards.map((card) => (
        <div
          key={card.label}
          className="rounded-xl border border-border bg-surface px-4 py-4"
        >
          <p className="text-xs font-medium text-muted uppercase tracking-wide mb-1">{card.label}</p>
          <p className={`text-2xl font-bold tabular-nums ${card.accent}`}>{card.value}</p>
          {card.sub ? (
            <p className="mt-0.5 text-xs text-muted">{card.sub}</p>
          ) : null}
        </div>
      ))}
    </div>
  );
}
