'use client';

import { useEffect, useState } from 'react';
import { Brain, Layers, BookOpen, Flame, Sparkles, ArrowRight, Heart } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { Badge } from '@/components/ui/badge';
import { CardLink } from '@/components/ui/card-link';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { RevisionPlanCard } from '@/components/domain/recalls/revision-plan-card';
import { WeeklyReportCard } from '@/components/domain/recalls/weekly-report-card';
import { fetchVocabularyStats, fetchReviewSummary } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyStats } from '@/lib/types/vocabulary';

type ReviewSummary = { due: number; total: number; dueToday: number; mastered: number; upcoming?: number };

/**
 * Recalls — unified home for vocabulary cards and spaced-repetition review.
 *
 * Phase 0 (this PR): aggregator landing page that surfaces both today's vocab
 * due-set and today's generic review queue, and routes the learner into the
 * existing /vocabulary and /review flows. Subsequent phases migrate those
 * flows under /recalls/* per docs/RECALLS-MODULE-PLAN.md.
 */
export default function RecallsHomePage() {
  const [vocab, setVocab] = useState<VocabularyStats | null>(null);
  const [review, setReview] = useState<ReviewSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('recalls_home_viewed');
    Promise.allSettled([fetchVocabularyStats(), fetchReviewSummary()]).then(([v, r]) => {
      if (v.status === 'fulfilled') setVocab(v.value as VocabularyStats);
      if (r.status === 'fulfilled') setReview(r.value as ReviewSummary);
      if (v.status === 'rejected' && r.status === 'rejected') setError('Could not load your recall queue.');
      setLoading(false);
    });
  }, []);

  const dueToday = (vocab?.dueToday ?? 0) + (review?.dueToday ?? 0);
  const mastered = (vocab?.mastered ?? 0) + (review?.mastered ?? 0);
  const total = (vocab?.totalInList ?? 0) + (review?.total ?? 0);
  const streak = vocab?.streakDays ?? 0;

  const heroHighlights = [
    { icon: Brain, label: 'Due today', value: `${dueToday}` },
    { icon: Layers, label: 'Mastered', value: `${mastered}` },
    { icon: BookOpen, label: 'Total', value: `${total}` },
    { icon: Flame, label: 'Streak', value: `${streak}d` },
  ];

  const tabs = [
    {
      href: '/recalls/words',
      eyebrow: 'Words',
      title: 'Vocabulary banks & flashcards',
      description: 'Curated medical terms, daily set, browse, type-to-spell, and quiz formats.',
      icon: <BookOpen className="h-6 w-6" aria-hidden="true" />,
      tile: 'bg-info/10 text-info',
      badge: vocab?.dueToday ? `${vocab.dueToday} due` : null,
    },
    {
      href: '/recalls/favourites',
      eyebrow: 'Favourites',
      title: 'Saved words to review later',
      description: 'Every word you favourited, in one place — ready to revisit or drill.',
      icon: <Heart className="h-6 w-6" aria-hidden="true" />,
      tile: 'bg-warning/10 text-warning-strong',
      badge: null,
    },
  ];

  return (
    <>
      <LearnerPageHero
        eyebrow="Recalls"
        title="Everything you are trying to remember, in one place"
        description="Vocabulary cards and spaced-repetition review unified. Click. Listen. Type. Star. Revise. Master."
        icon={Sparkles}
        highlights={heroHighlights}
      />

      <LearnerSkillSwitcher compact />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Today"
          title="Pick a mode to start your recall session"
          description="Each tab routes into the same SM-2 engine; your progress is shared across vocabulary and review."
        />

        {loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {Array.from({ length: 2 }).map((_, i) => (
              <Skeleton key={i} className="h-32 rounded-2xl" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {tabs.map((t, i) => (
              <MotionItem key={t.href} delayIndex={i} className="h-full">
                <CardLink href={t.href} className="group flex h-full items-start gap-4">
                  <div className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-xl ${t.tile}`}>
                    {t.icon}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="eyebrow text-muted">{t.eyebrow}</p>
                    <div className="mt-1 flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-navy">{t.title}</span>
                      {t.badge && <Badge variant="warning">{t.badge}</Badge>}
                    </div>
                    <p className="mt-1 text-sm text-muted">{t.description}</p>
                  </div>
                  <ArrowRight className="mt-1 h-5 w-5 shrink-0 text-muted transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden="true" />
                </CardLink>
              </MotionItem>
            ))}
          </div>
        )}
      </MotionSection>

      <MotionSection delayIndex={1} className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <RevisionPlanCard />
        <WeeklyReportCard />
      </MotionSection>
    </>
  );
}
