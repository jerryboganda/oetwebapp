'use client';

import type { ReactNode } from 'react';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';

interface DashboardHeroProps {
  readinessScore: number;
  predictedScore: number | null;
  daysToExam: number | null;
  streak: number;
}

interface StatTile {
  label: string;
  value: ReactNode;
  sub?: string;
  accent: string;
}

/** Counts up whole numbers; anything else (e.g. 72.5) is shown exactly as before. */
function metric(value: number, suffix = ''): ReactNode {
  return Number.isInteger(value) ? <CountUp value={value} suffix={suffix} /> : `${value}${suffix}`;
}

export function DashboardHero({ readinessScore, predictedScore, daysToExam, streak }: DashboardHeroProps) {
  const cards: StatTile[] = [
    {
      label: 'Readiness',
      value: metric(readinessScore, '%'),
      sub: readinessScore >= 80 ? 'Exam ready' : readinessScore >= 60 ? 'Getting there' : 'Keep going',
      accent: readinessScore >= 80 ? 'text-success-strong' : readinessScore >= 60 ? 'text-warning-strong' : 'text-danger-strong',
    },
    {
      label: 'AI Practice Score',
      value: predictedScore != null && predictedScore > 0 ? metric(predictedScore) : '–',
      sub: predictedScore != null && predictedScore > 0 ? 'Not an official OET result' : 'No data yet',
      accent: 'text-muted',
    },
    // Only with a real exam date: an always-"No exam set" tile would contradict a
    // learner who has set one.
    ...(daysToExam != null
      ? [{
        label: 'Days to Exam',
        value: metric(daysToExam),
        sub: daysToExam <= 14 ? 'Final sprint!' : `${Math.ceil(daysToExam / 7)} weeks`,
        accent: daysToExam <= 14 ? 'text-danger-strong' : 'text-muted',
      }]
      : []),
    {
      label: 'Streak',
      value: streak > 0 ? metric(streak, ` day${streak === 1 ? '' : 's'}`) : '0 days',
      sub: streak >= 7 ? 'On fire!' : streak > 0 ? 'Keep it up' : 'Start today',
      accent: streak >= 7 ? 'text-warning-strong' : 'text-muted',
    },
  ];

  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3">
      {cards.map((card) => (
        <Card key={card.label} className="min-w-0">
          <p className="eyebrow mb-1 break-words text-muted">{card.label}</p>
          <p className={`text-2xl font-bold tabular-nums ${card.accent}`}>{card.value}</p>
          {card.sub ? (
            <p className="mt-0.5 text-xs text-muted">{card.sub}</p>
          ) : null}
        </Card>
      ))}
    </div>
  );
}
