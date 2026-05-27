'use client';

import { CalendarCheck, Clock, DollarSign, Users } from 'lucide-react';

import { StatCard } from '@/components/ui/stat-card';

export interface DashboardCardsProps {
  nextSessionTitle: string | null;
  nextSessionStartsAt: string | null;
  hoursToNext: number | null;
  weekClasses: number;
  weekEnrollments: number;
  netUsd: number;
}

function nextLabel(hours: number | null, startsAt: string | null) {
  if (hours == null || !startsAt) return 'No upcoming class';
  if (hours === 0) return 'Starting now';
  if (hours === 1) return 'In 1 hour';
  if (hours < 24) return `In ${hours} hours`;
  const days = Math.round(hours / 24);
  return days === 1 ? 'Tomorrow' : `In ${days} days`;
}

export function DashboardCards({
  nextSessionTitle,
  nextSessionStartsAt,
  hoursToNext,
  weekClasses,
  weekEnrollments,
  netUsd,
}: DashboardCardsProps) {
  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <StatCard
        label="Next class"
        value={nextLabel(hoursToNext, nextSessionStartsAt)}
        hint={nextSessionTitle ?? 'Schedule a class to see it here.'}
        icon={<Clock className="h-3.5 w-3.5" />}
        tone="info"
      />
      <StatCard
        label="This week"
        value={`${weekClasses} ${weekClasses === 1 ? 'class' : 'classes'}`}
        hint="Sessions scheduled in the next 7 days."
        icon={<CalendarCheck className="h-3.5 w-3.5" />}
        tone="default"
      />
      <StatCard
        label="Enrollments"
        value={String(weekEnrollments)}
        hint="Learners booked this week."
        icon={<Users className="h-3.5 w-3.5" />}
        tone="success"
      />
      <StatCard
        label="Net earnings"
        value={`$${netUsd.toFixed(0)}`}
        hint="Lifetime, after platform share."
        icon={<DollarSign className="h-3.5 w-3.5" />}
        tone="default"
      />
    </div>
  );
}
