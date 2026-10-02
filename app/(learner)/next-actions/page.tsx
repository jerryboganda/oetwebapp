'use client';

import { useCallback, useEffect, useState, type ElementType } from 'react';
import Link from 'next/link';
import { Sparkles, ArrowRight, Clock, AlertTriangle, Trophy, Target, CheckCircle2 } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { cn } from '@/lib/utils';

interface NextAction {
  type: string;
  priority: string;
  title: string;
  subtitle: string;
  actionUrl: string;
  subtestCode: string | null;
}

interface NextActionsData {
  actions: NextAction[];
  generatedAt: string;
}

const apiRequest = apiClient.request;

// Priority stays readable at a glance (status border, icon tile, status badge) without
// painting every card in a full red/amber/green tint.
const PRIORITY_STYLES: Record<string, { border: string; tile: string; badge: BadgeProps['variant']; icon: ElementType }> = {
  high: { border: 'border-danger/30', tile: 'bg-danger/10 text-danger-strong', badge: 'danger', icon: AlertTriangle },
  medium: { border: 'border-warning/30', tile: 'bg-warning/10 text-warning-strong', badge: 'warning', icon: Target },
  low: { border: 'border-success/30', tile: 'bg-success/10 text-success-strong', badge: 'success', icon: CheckCircle2 },
};

const TYPE_ICONS: Record<string, ElementType> = {
  overdue_task: Clock,
  review_ready: Trophy,
  weak_area_practice: Target,
  exam_approaching: AlertTriangle,
  daily_goal: CheckCircle2,
};

export default function NextActionsPage() {
  const [data, setData] = useState<NextActionsData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    apiRequest<NextActionsData>('/v1/learner/next-actions')
      .then(setData)
      .catch(() => setError('Unable to load recommendations.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('content_view', { page: 'next-actions' });
    load();
  }, [load]);

  return (
    <>
      <LearnerPageHero
        title="What to Do Next"
        description="AI-powered recommendations based on your goals, performance, and exam timeline."
        icon={Sparkles}
      />

      {loading ? <LearnerSkeleton variant="list" /> : null}

      {!loading && error && (
        <InlineAlert
          variant="error"
          title="Error"
          action={<Button size="sm" variant="outline" onClick={load}>Retry</Button>}
        >
          {error}
        </InlineAlert>
      )}

      {!loading && data && data.actions.length === 0 && (
        <LearnerEmptyState
          icon={CheckCircle2}
          title="All caught up!"
          description="No immediate actions needed. Keep up your daily practice."
          primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
        />
      )}

      {!loading && data && data.actions.length > 0 && (
        <section>
          <div className="space-y-4">
            {data.actions.map((action, i) => {
              const style = PRIORITY_STYLES[action.priority] ?? PRIORITY_STYLES.low;
              const PriorityIcon = style.icon;
              const TypeIcon = TYPE_ICONS[action.type];
              return (
                <MotionItem key={i} delayIndex={Math.min(i, 5)}>
                  <Card className={cn('flex items-start gap-3 sm:gap-4', style.border)}>
                    <span className={cn('flex h-10 w-10 shrink-0 items-center justify-center rounded-xl', style.tile)}>
                      <PriorityIcon className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <div className="min-w-0 flex-1">
                      <div className="mb-1 flex items-center gap-2">
                        {TypeIcon ? <TypeIcon className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" /> : null}
                        <h2 className="min-w-0 font-semibold text-navy">{action.title}</h2>
                      </div>
                      <p className="text-sm text-muted">{action.subtitle}</p>
                      <div className="mt-2 flex flex-wrap items-center gap-2">
                        <Badge variant={style.badge} className="capitalize">{action.priority} priority</Badge>
                        {action.subtestCode && <Badge variant="outline" className="capitalize">{action.subtestCode}</Badge>}
                      </div>
                    </div>
                    <Button asChild size="sm" className="shrink-0">
                      <Link href={action.actionUrl} aria-label={`Go: ${action.title}`}>
                        Go <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                      </Link>
                    </Button>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
          <p className="mt-4 text-end text-xs text-muted">
            Generated {data.generatedAt ? new Date(data.generatedAt).toLocaleTimeString() : 'now'}
          </p>
        </section>
      )}

      <MotionSection>
        <InlineAlert variant="info" title="How it works">
          Recommendations consider your study plan, pending reviews, weak areas, exam proximity, and engagement patterns.
          Check back regularly for updated guidance.
        </InlineAlert>
      </MotionSection>
    </>
  );
}
