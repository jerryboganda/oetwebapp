'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ArrowRight, CheckCircle2, ListChecks, RefreshCcw, SkipForward } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import {
  completeWritingTodayItem,
  getWritingTodayPlanV2,
  regenerateWritingTodayPlan,
} from '@/lib/writing/api';
import {
  completeWritingPlanItem,
  getWritingTodayPlan,
  skipWritingPlanItem,
  startWritingPlanItem,
  type WritingTodayPlanDto as WritingTodayPlanV1Dto,
} from '@/lib/writing-pathway-api';
import type {
  WritingTodayPlanDto as WritingTodayPlanV2Dto,
  WritingTodayPlanItemDto,
} from '@/lib/writing/types';

interface ResolvedPlan {
  source: 'v2' | 'v1';
  date: string;
  items: WritingTodayPlanItemDto[];
  totalMinutes: number;
  completedCount: number;
  regenerationsRemaining: number;
}

function fromV2(plan: WritingTodayPlanV2Dto): ResolvedPlan {
  return {
    source: 'v2',
    date: plan.date,
    items: plan.items,
    totalMinutes: plan.totalMinutes,
    completedCount: plan.completedCount,
    regenerationsRemaining: plan.regenerationsRemaining,
  };
}

function fromV1(plan: WritingTodayPlanV1Dto): ResolvedPlan {
  return {
    source: 'v1',
    date: plan.date,
    items: plan.items.map<WritingTodayPlanItemDto>((item, idx) => ({
      id: item.id,
      ordinal: idx + 1,
      itemKind: 'letter',
      focusSkill: null,
      focusCriterion: null,
      estimatedMinutes: item.estimatedMinutes,
      title: item.title,
      description: item.description,
      actionHref: item.actionHref,
      contentId: item.contentId ?? null,
      status: item.status === 'completed' ? 'completed' : item.status === 'in-progress' ? 'in-progress' : item.status === 'skipped' ? 'skipped' : 'pending',
    })),
    totalMinutes: plan.totalMinutes,
    completedCount: plan.completedCount,
    regenerationsRemaining: 1,
  };
}

export default function WritingTodayPage() {
  const t = useTranslations();
  const router = useRouter();
  const [plan, setPlan] = useState<ResolvedPlan | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyAction, setBusyAction] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const v2 = await getWritingTodayPlanV2();
      if (v2 && Array.isArray(v2.items) && v2.items.length > 0) {
        setPlan(fromV2(v2));
        return;
      }
    } catch {
      /* fall through */
    }
    try {
      const v1 = await getWritingTodayPlan();
      setPlan(fromV1(v1));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.today.error.load'));
    }
  }, [t]);

  useEffect(() => {
    void load();
  }, [load]);

  const onStart = async (item: WritingTodayPlanItemDto) => {
    setBusyAction(`start-${item.id}`);
    try {
      if (plan?.source === 'v1') await startWritingPlanItem(item.id);
      router.push(item.actionHref);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.today.error.start'));
    } finally {
      setBusyAction(null);
    }
  };

  const onComplete = async (item: WritingTodayPlanItemDto) => {
    setBusyAction(`complete-${item.id}`);
    try {
      if (plan?.source === 'v2') await completeWritingTodayItem(item.id);
      else await completeWritingPlanItem(item.id);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.today.error.complete'));
    } finally {
      setBusyAction(null);
    }
  };

  const onSkip = async (item: WritingTodayPlanItemDto) => {
    if (plan?.source !== 'v1') return; // V2 doesn't expose skip yet
    setBusyAction(`skip-${item.id}`);
    try {
      await skipWritingPlanItem(item.id);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.today.error.skip'));
    } finally {
      setBusyAction(null);
    }
  };

  const onRegenerate = async () => {
    setBusyAction('regen');
    try {
      const next = await regenerateWritingTodayPlan();
      setPlan(fromV2(next));
    } catch (err) {
      setError(err instanceof Error ? err.message : t('writing.today.error.regenerate'));
    } finally {
      setBusyAction(null);
    }
  };

  return (
    <LearnerDashboardShell pageTitle={t('writing.today.pageTitle')}>
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow={t('writing.today.eyebrow')}
          icon={ListChecks}
          accent="amber"
          title={t('writing.today.title')}
          description={t('writing.today.hero.description')}
          highlights={[
            { icon: ListChecks, label: t('writing.today.highlights.items'), value: `${plan?.items.length ?? 0}` },
            { icon: CheckCircle2, label: t('writing.today.highlights.completed'), value: `${plan?.completedCount ?? 0}` },
            { icon: ArrowRight, label: t('writing.today.highlights.minutes'), value: `${plan?.totalMinutes ?? 0}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow={plan?.source === 'v2' ? t('writing.today.section.v2') : t('writing.today.section.v1')}
            title={plan ? new Date(`${plan.date}T00:00:00`).toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' }) : 'Loading'}
            description={t('writing.today.section.description')}
            action={
              plan?.source === 'v2' ? (
                <Button onClick={() => void onRegenerate()} loading={busyAction === 'regen'} disabled={plan.regenerationsRemaining <= 0} size="sm" variant="outline">
                  <RefreshCcw className="h-4 w-4" aria-hidden="true" /> {t('writing.today.regenerateLabel', { remaining: plan.regenerationsRemaining })}
                </Button>
              ) : undefined
            }
            className="mb-5"
          />

          <ol className="divide-y divide-border rounded-xl border border-border bg-background" aria-label={t('writing.today.title')}>
            {(plan?.items ?? []).map((item) => (
              <li key={item.id} className="flex flex-col gap-4 p-4 lg:flex-row lg:items-center lg:justify-between">
                <div className="space-y-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant={item.status === 'completed' ? 'success' : item.status === 'skipped' ? 'muted' : 'warning'} size="sm">{item.status}</Badge>
                    <Badge variant="info" size="sm">{item.estimatedMinutes} min</Badge>
                    {item.focusSkill ? <Badge variant="muted" size="sm">{item.focusSkill}</Badge> : null}
                    <Badge variant="muted" size="sm" className="capitalize">{item.itemKind}</Badge>
                  </div>
                  {/*
                    Item title + description come from the backend plan generator
                    (English content per spec §32). Force LTR so they render
                    correctly when the surrounding chrome is RTL.
                  */}
                  <h2 className="text-lg font-bold text-navy" dir="ltr">{item.title}</h2>
                  <p className="max-w-3xl text-sm text-muted" dir="ltr">{item.description}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <Button size="sm" onClick={() => void onStart(item)} loading={busyAction === `start-${item.id}`}>
                    {t('writing.today.actions.start')} <ArrowRight className="h-4 w-4" aria-hidden="true" />
                  </Button>
                  {item.status !== 'completed' ? (
                    <Button size="sm" variant="outline" onClick={() => void onComplete(item)} loading={busyAction === `complete-${item.id}`}>
                      {t('writing.today.actions.complete')}
                    </Button>
                  ) : null}
                  {plan?.source === 'v1' && item.status === 'pending' ? (
                    <Button size="sm" variant="ghost" onClick={() => void onSkip(item)} loading={busyAction === `skip-${item.id}`}>
                      <SkipForward className="h-4 w-4" aria-hidden="true" /> {t('writing.today.actions.skip')}
                    </Button>
                  ) : null}
                </div>
              </li>
            ))}
          </ol>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
