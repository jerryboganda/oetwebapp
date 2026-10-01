'use client';

import { useCallback, useEffect, useState } from 'react';
import { Sparkles, ArrowRight, Clock, AlertTriangle, Trophy, Target, CheckCircle2 } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

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

const PRIORITY_STYLES: Record<string, { border: string; bg: string; icon: React.ReactNode }> = {
  high: { border: 'border-danger/30', bg: 'bg-danger/10', icon: <AlertTriangle className="w-5 h-5 text-danger-strong" /> },
  medium: { border: 'border-warning/30', bg: 'bg-warning/10', icon: <Target className="w-5 h-5 text-warning-strong" /> },
  low: { border: 'border-success/30', bg: 'bg-success/10', icon: <CheckCircle2 className="w-5 h-5 text-success-strong" /> },
};

const TYPE_ICONS: Record<string, React.ReactNode> = {
  overdue_task: <Clock className="w-4 h-4" />,
  review_ready: <Trophy className="w-4 h-4" />,
  weak_area_practice: <Target className="w-4 h-4" />,
  exam_approaching: <AlertTriangle className="w-4 h-4" />,
  daily_goal: <CheckCircle2 className="w-4 h-4" />,
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

  if (loading) {
    return (
      <>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24 rounded-xl" />)}
        </div>
      </>
    );
  }

  return (
    <>
      <LearnerPageHero
        title="What to Do Next"
        description="AI-powered recommendations based on your goals, performance, and exam timeline."
        icon={<Sparkles className="w-7 h-7" />}
      />

      {error && (
        <InlineAlert
          variant="error"
          title="Error"
          action={<Button size="sm" variant="outline" onClick={load}>Retry</Button>}
        >
          {error}
        </InlineAlert>
      )}

      {data && data.actions.length === 0 && (
        <LearnerEmptyState
          icon={CheckCircle2}
          title="All caught up!"
          description="No immediate actions needed. Keep up your daily practice."
          primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
        />
      )}

      {data && data.actions.length > 0 && (
        <MotionSection>
          <div className="space-y-4">
            {data.actions.map((action, i) => {
              const style = PRIORITY_STYLES[action.priority] ?? PRIORITY_STYLES.low;
              return (
                <MotionItem key={i}>
                  <Card className={`p-5 border-2 ${style.border} ${style.bg}`}>
                    <div className="flex items-start gap-4">
                      <div className="flex-shrink-0 mt-0.5">{style.icon}</div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          {TYPE_ICONS[action.type]}
                          <h3 className="font-semibold text-navy">{action.title}</h3>
                        </div>
                        <p className="text-sm text-muted">{action.subtitle}</p>
                        <div className="flex items-center gap-2 mt-2">
                          <Badge variant="outline" className="capitalize">{action.priority} priority</Badge>
                          {action.subtestCode && <Badge variant="outline" className="capitalize">{action.subtestCode}</Badge>}
                        </div>
                      </div>
                      <Button asChild size="sm" className="shrink-0">
                        <a href={action.actionUrl} aria-label={`Go: ${action.title}`}>
                          Go <ArrowRight className="w-4 h-4" aria-hidden="true" />
                        </a>
                      </Button>
                    </div>
                  </Card>
                </MotionItem>
              );
            })}
          </div>
          <p className="text-xs text-muted/60 mt-4 text-right">
            Generated {data.generatedAt ? new Date(data.generatedAt).toLocaleTimeString() : 'now'}
          </p>
        </MotionSection>
      )}

      <MotionSection className="mt-6">
        <InlineAlert variant="info" title="How it works">
          Recommendations consider your study plan, pending reviews, weak areas, exam proximity, and engagement patterns.
          Check back regularly for updated guidance.
        </InlineAlert>
      </MotionSection>
    </>
  );
}
