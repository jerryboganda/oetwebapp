'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  CalendarDays,
  ChevronRight,
  Trophy,
  Sparkles,
  Headphones,
  Target,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  accentLabel,
  getListeningPathway,
  skillLabel,
  type Pathway,
  type RoadmapWeek,
} from '@/lib/listening-pathway-api';

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function phaseStyle(phase: string): {
  badge: string;
  border: string;
  bg: string;
  text: string;
} {
  switch (phase) {
    case 'foundation':
      return {
        badge: 'bg-sky-100 text-sky-800 ring-1 ring-sky-200',
        border: 'border-sky-200',
        bg: 'bg-sky-50/50',
        text: 'text-sky-900',
      };
    case 'practice':
      return {
        badge: 'bg-amber-100 text-amber-800 ring-1 ring-amber-200',
        border: 'border-amber-200',
        bg: 'bg-amber-50/40',
        text: 'text-amber-900',
      };
    case 'mastery':
      return {
        badge: 'bg-emerald-100 text-emerald-800 ring-1 ring-emerald-200',
        border: 'border-emerald-200',
        bg: 'bg-emerald-50/40',
        text: 'text-emerald-900',
      };
    default:
      return {
        badge: 'bg-violet-100 text-violet-800 ring-1 ring-violet-200',
        border: 'border-violet-200',
        bg: 'bg-violet-50/40',
        text: 'text-violet-900',
      };
  }
}

function RoadmapWeekCard({ week }: { week: RoadmapWeek }) {
  const style = phaseStyle(week.phase);
  return (
    <article
      className={`flex flex-col gap-3 rounded-2xl border ${style.border} ${style.bg} p-5 shadow-sm`}
      aria-label={`Week ${week.weekNumber}, ${week.phase} phase`}
    >
      <header className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-extrabold text-gray-900">Week {week.weekNumber}</h3>
        <span
          className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-wide ${style.badge}`}
        >
          {week.phase}
        </span>
      </header>

      {week.focusSkills.length > 0 && (
        <div>
          <p className="mb-1 flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-gray-500">
            <Target className="h-3 w-3" aria-hidden />
            Focus skills
          </p>
          <div className="flex flex-wrap gap-1">
            {week.focusSkills.map((skill) => (
              <span
                key={skill}
                title={skillLabel(skill)}
                className="rounded-md bg-white px-1.5 py-0.5 text-[11px] font-semibold text-violet-700 ring-1 ring-violet-100"
              >
                {skill}
              </span>
            ))}
          </div>
        </div>
      )}

      {week.focusAccents.length > 0 && (
        <div>
          <p className="mb-1 flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wide text-gray-500">
            <Headphones className="h-3 w-3" aria-hidden />
            Accents
          </p>
          <div className="flex flex-wrap gap-1">
            {week.focusAccents.map((accent) => (
              <span
                key={accent}
                className="rounded-md bg-white px-1.5 py-0.5 text-[11px] font-medium text-amber-700 ring-1 ring-amber-100"
              >
                {accentLabel(accent)}
              </span>
            ))}
          </div>
        </div>
      )}

      <div className="mt-auto flex items-center justify-between border-t border-white/60 pt-3 text-[11px] text-gray-600">
        <span className="font-semibold">{week.dailyMinutes} min / day</span>
        {week.mockAtEndOfWeek ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 font-bold text-emerald-800">
            <Trophy className="h-3 w-3" aria-hidden />
            Mock
          </span>
        ) : (
          <span className="text-gray-400">No mock</span>
        )}
      </div>

      {week.notes && <p className={`text-xs leading-relaxed ${style.text}`}>{week.notes}</p>}
    </article>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Page
// ─────────────────────────────────────────────────────────────────────────────

export default function ListeningPathwayPage() {
  const [pathway, setPathway] = useState<Pathway | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await getListeningPathway();
        if (!cancelled) setPathway(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load pathway.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const mockCount = useMemo(
    () => pathway?.weeks.filter((w) => w.mockAtEndOfWeek).length ?? 0,
    [pathway],
  );

  return (
    <LearnerDashboardShell pageTitle="Listening Pathway">
      <div className="mx-auto max-w-5xl space-y-8">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Your listening roadmap</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            A personalised 12-week schedule of focus skills, accent practice, and mock tests.
          </p>
        </div>

        {pathway && (
          <div className="flex flex-wrap gap-6 rounded-2xl border border-border bg-surface p-5 text-sm">
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted-foreground">
                Total weeks
              </span>
              <span className="text-xl font-bold text-primary">{pathway.totalWeeks}</span>
            </div>
            <div>
              <span className="block text-xs uppercase tracking-wide text-muted-foreground">
                Mocks scheduled
              </span>
              <span className="text-xl font-bold text-foreground">{mockCount}</span>
            </div>
            <div className="flex items-center gap-2">
              <CalendarDays className="h-4 w-4 text-muted-foreground" aria-hidden />
              <span className="text-sm text-muted-foreground">
                Generated{' '}
                {pathway.generatedAt
                  ? new Intl.DateTimeFormat('en-GB', {
                      day: '2-digit',
                      month: 'short',
                      year: 'numeric',
                    }).format(new Date(pathway.generatedAt))
                  : 'recently'}
              </span>
            </div>
          </div>
        )}

        {error ? (
          <div className="space-y-4">
            <InlineAlert variant="error">{error}</InlineAlert>
            <Link
              href="/listening"
              className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            >
              Back to Listening
              <ChevronRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        ) : loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-44 w-full rounded-2xl" />
            ))}
          </div>
        ) : pathway && pathway.weeks.length > 0 ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {pathway.weeks.map((week) => (
              <RoadmapWeekCard key={week.weekNumber} week={week} />
            ))}
          </div>
        ) : (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <Sparkles className="mx-auto mb-3 h-8 w-8 text-muted-foreground" aria-hidden />
            <p className="font-semibold text-foreground">No pathway generated yet</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Complete the listening diagnostic to unlock your personalised 12-week plan.
            </p>
            <Link
              href="/listening/diagnostic"
              className="mt-4 inline-flex items-center gap-1 rounded-xl bg-primary px-4 py-2 text-sm font-bold text-white hover:bg-primary/90"
            >
              Take the diagnostic
              <ChevronRight className="h-4 w-4" aria-hidden />
            </Link>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
