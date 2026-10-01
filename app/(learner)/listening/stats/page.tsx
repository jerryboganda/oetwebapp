'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BarChart3, CalendarDays, Globe2, Radar } from 'lucide-react';
import { getSkillScores, getAccentProgress, getListeningPathway } from '@/lib/listening-pathway-api';
import type { SkillScore, AccentProgress, Pathway } from '@/lib/listening-pathway-api';
import { SkillRadarChart } from '@/components/listening/SkillRadarChart';
import { AccentBarChart } from '@/components/listening/AccentBarChart';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { CardSkeleton, Skeleton } from '@/components/ui/skeleton';

export default function ListeningStatsPage() {
  const [skills, setSkills] = useState<SkillScore[]>([]);
  const [accents, setAccents] = useState<AccentProgress[]>([]);
  const [pathway, setPathway] = useState<Pathway | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([getSkillScores(), getAccentProgress(), getListeningPathway()])
      .then(([s, a, p]) => {
        if (cancelled) return;
        if (s.status === 'fulfilled') setSkills(s.value);
        if (a.status === 'fulfilled') setAccents(a.value);
        if (p.status === 'fulfilled') setPathway(p.value);
        const firstErr = [s, a, p].find((r) => r.status === 'rejected');
        if (firstErr && firstErr.status === 'rejected') {
          setError(firstErr.reason instanceof Error ? firstErr.reason.message : String(firstErr.reason));
        }
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return (
      <div aria-busy="true" className="learner-page-flow">
        <p className="sr-only">Loading your Listening stats…</p>
        <Skeleton className="h-32 rounded-2xl" />
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          <CardSkeleton className="h-80" />
          <CardSkeleton className="h-80" />
        </div>
        <CardSkeleton />
      </div>
    );
  }

  return (
    <>
      <LearnerPageHero
        icon={BarChart3}
        accent="purple"
        title="Listening Analytics"
        description="Track your sub-skill mastery (L1–L8) and accent confidence over time."
      />

      {error ? (
        <InlineAlert variant="warning">
          Some metrics could not be loaded: {error}. Take the diagnostic if you haven&apos;t already.
        </InlineAlert>
      ) : null}

      <MotionSection delayIndex={0}>
        <section className="grid grid-cols-1 gap-6 md:grid-cols-2">
          <Card padding="lg">
            <LearnerSurfaceSectionHeader title="Sub-skill mastery (L1–L8)" className="mb-4" />
            {skills.length > 0 ? (
              <>
                <SkillRadarChart scores={skills} />
                <ul className="mt-4 divide-y divide-border text-sm text-muted">
                  {skills.map((s) => (
                    <li key={s.skillCode} className="flex justify-between gap-3 py-1.5">
                      <span>{s.label}</span>
                      <span className="shrink-0 tabular-nums text-navy">{s.currentScore.toFixed(1)} / 10</span>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <EmptyState
                className="py-8"
                icon={<Radar className="h-7 w-7" aria-hidden />}
                title="Skill scores will appear after your first diagnostic."
              />
            )}
          </Card>

          <Card padding="lg">
            <LearnerSurfaceSectionHeader title="Accent confidence" className="mb-4" />
            {accents.length > 0 ? (
              <AccentBarChart accents={accents} />
            ) : (
              <EmptyState
                className="py-8"
                icon={<Globe2 className="h-7 w-7" aria-hidden />}
                title="Accent breakdown will appear after your first diagnostic."
              />
            )}
          </Card>
        </section>
      </MotionSection>

      <MotionSection delayIndex={1}>
        <Card padding="lg">
          <LearnerSurfaceSectionHeader title="Your 12-week roadmap" className="mb-4" />
          {pathway && pathway.weeks.length > 0 ? (
            <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
              {pathway.weeks.map((w, index) => (
                <li key={w.weekNumber}>
                  <MotionItem
                    delayIndex={Math.min(index, 5)}
                    className="h-full rounded-xl border border-border bg-background-light p-3 text-sm"
                  >
                    <div className="mb-1 flex items-center justify-between gap-2">
                      <span className="font-semibold tabular-nums text-navy">Week {w.weekNumber}</span>
                      <span className="eyebrow text-muted">{w.phase}</span>
                    </div>
                    <p className="text-muted">{w.notes}</p>
                    <p className="mt-1 text-xs tabular-nums text-muted">~{w.dailyMinutes} min/day</p>
                  </MotionItem>
                </li>
              ))}
            </ul>
          ) : (
            <EmptyState
              className="py-8"
              icon={<CalendarDays className="h-7 w-7" aria-hidden />}
              title="Your roadmap will be generated after the diagnostic."
              action={{ label: 'Take the diagnostic', href: '/listening/diagnostic' }}
            />
          )}
        </Card>
      </MotionSection>

      <div className="flex flex-wrap gap-3">
        <Button asChild size="sm">
          <Link href="/listening">Back to dashboard</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link href="/listening/pathway">View full pathway</Link>
        </Button>
      </div>
    </>
  );
}
