'use client';

import { useEffect, useState } from 'react';
import { BookOpen, ChevronRight, CheckCircle2, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface PathItem {
  id: string;
  title: string;
  difficulty: string;
  durationMinutes: number;
  completed: boolean;
  scenarioType: string | null;
}

interface SubtestPath {
  subtestCode: string;
  totalItems: number;
  completedItems: number;
  progressPercent: number;
  items: PathItem[];
}

interface LearningPathData {
  professionCode: string;
  professionLabel: string;
  examTypeCode: string;
  subtestPaths: SubtestPath[];
  overallProgress: number;
  totalContent: number;
  nextRecommended: { id: string; title: string; subtestCode: string; difficulty: string }[];
}

const apiRequest = apiClient.request;

const DIFFICULTY_COLORS: Record<string, string> = {
  easy: 'text-success bg-success/10',
  medium: 'text-warning bg-warning/10',
  hard: 'text-danger bg-danger/10',
};

export default function LearningPathsPage() {
  const [data, setData] = useState<LearningPathData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'learning-paths' });
    apiRequest<LearningPathData>('/v1/learner/learning-path')
      .then(setData)
      .catch(() => setError('Unable to load learning path.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-60" />
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-40 rounded-xl" />)}
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Your Learning Path"
        description={data ? `${data.professionLabel} · ${data.examTypeCode.toUpperCase()} · ${data.overallProgress}% complete` : 'Personalized by your profession and goals'}
        icon={<BookOpen className="w-7 h-7" />}
      />

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* Overall progress */}
      {data && (
        <MotionSection>
          <Card className="p-5">
            <div className="flex items-center justify-between mb-3">
              <h3 className="font-semibold text-navy">Overall Progress</h3>
              <span className="text-2xl font-bold text-primary">{data.overallProgress}%</span>
            </div>
            <div className="h-3 bg-border rounded-full overflow-hidden">
              <div className="h-full bg-primary rounded-full transition-all" style={{ width: `${data.overallProgress}%` }} />
            </div>
            <p className="text-xs text-muted mt-2">{data.totalContent} total content items across all subtests</p>
          </Card>
        </MotionSection>
      )}

      {/* Next recommended */}
      {data && data.nextRecommended.length > 0 && (
        <MotionSection className="mt-6">
          <LearnerSurfaceSectionHeader icon={<Target className="w-5 h-5" />} title="Recommended Next" />
          <div className="grid gap-3 sm:grid-cols-3 mt-3">
            {data.nextRecommended.map((rec) => (
              <MotionItem key={rec.id}>
                <Card className="p-4 hover:shadow-md transition-shadow cursor-pointer">
                  <Badge className={DIFFICULTY_COLORS[rec.difficulty] ?? ''}>{rec.difficulty}</Badge>
                  <p className="font-medium mt-2 text-sm text-navy">{rec.title}</p>
                  <p className="text-xs text-muted capitalize">{rec.subtestCode}</p>
                </Card>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      )}

      {/* Per-subtest paths */}
      {data?.subtestPaths.map((sp) => (
        <MotionSection key={sp.subtestCode} className="mt-6">
          <LearnerSurfaceSectionHeader
            icon={<BookOpen className="w-5 h-5" />}
            title={sp.subtestCode.charAt(0).toUpperCase() + sp.subtestCode.slice(1)}
          />
          <div className="flex items-center gap-3 mt-2 mb-4">
            <div className="flex-1 h-2 bg-border rounded-full overflow-hidden">
              <div className="h-full bg-primary rounded-full transition-all" style={{ width: `${sp.progressPercent}%` }} />
            </div>
            <span className="text-sm font-medium text-muted">{sp.completedItems}/{sp.totalItems}</span>
          </div>
          <div className="space-y-2">
            {sp.items.map((item) => (
              <MotionItem key={item.id}>
                <Card className={`p-3 flex items-center gap-3 ${item.completed ? 'opacity-60' : ''}`}>
                  {item.completed ? (
                    <CheckCircle2 className="w-5 h-5 text-success flex-shrink-0" />
                  ) : (
                    <ChevronRight className="w-5 h-5 text-muted/60 flex-shrink-0" />
                  )}
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-navy truncate">{item.title}</p>
                    <p className="text-xs text-muted">{item.durationMinutes}min · {item.difficulty}</p>
                  </div>
                  {!item.completed && (
                    <Button size="sm" variant="outline">Start</Button>
                  )}
                </Card>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      ))}
    </LearnerDashboardShell>
  );
}
