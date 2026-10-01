'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  BarChart2,
  ChevronDown,
  ChevronUp,
  Loader2,
  Plus,
  Users,
} from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Modal } from '@/components/ui/modal';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { apiClient } from '@/lib/api';
import { teacherClassApi } from '@/lib/listening/v2-api';

// ─── Types ──────────────────────────────────────────────────────────────────

interface TeacherClass {
  id: string;
  name: string;
  description: string | null;
  memberCount?: number;
  createdAt?: string;
}

interface LearnerBreakdown {
  userId: string;
  displayName: string | null;
  attemptCount: number;
  averageScore: number | null;
}

interface ClassAnalytics {
  classId: string;
  days: number;
  attemptCount: number;
  averageScore: number | null;
  learners?: LearnerBreakdown[];
}

// ─── Create Class Dialog ─────────────────────────────────────────────────────

interface CreateClassDialogProps {
  open: boolean;
  onClose: () => void;
  onCreate: (name: string, description: string) => Promise<void>;
}

function CreateClassDialog({ open, onClose, onCreate }: CreateClassDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async () => {
    if (!name.trim()) {
      setError('Class name is required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onCreate(name.trim(), description.trim());
      setName('');
      setDescription('');
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create class.');
    } finally {
      setBusy(false);
    }
  };

  const handleClose = () => {
    if (!busy) {
      setName('');
      setDescription('');
      setError(null);
      onClose();
    }
  };

  return (
    <Modal open={open} onClose={handleClose} title="Create Class" size="md">
      <div className="space-y-4">
        <Input
          label="Class name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="e.g. Morning OET Cohort"
        />
        <Textarea
          label="Description (optional)"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={2}
          placeholder="Short description for this class"
        />
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="flex justify-end gap-2 pt-1">
          <Button variant="ghost" onClick={handleClose} disabled={busy}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleSubmit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : <Plus className="h-4 w-4" aria-hidden />}
            Create
          </Button>
        </div>
      </div>
    </Modal>
  );
}

// ─── Analytics Panel ─────────────────────────────────────────────────────────

function AnalyticsPanel({
  analytics,
  loading,
  error,
}: {
  analytics: ClassAnalytics | null;
  loading: boolean;
  error: string | null;
}) {
  if (loading) return <Skeleton className="h-32 rounded-2xl mt-3" />;
  if (error) return <InlineAlert variant="error">{error}</InlineAlert>;
  if (!analytics) return null;

  return (
    <div className="mt-3 space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="min-w-0 rounded-xl bg-background-light p-3">
          <p className="eyebrow break-words text-muted">
            Attempts (last {analytics.days}d)
          </p>
          <p className="mt-1 text-2xl font-bold text-navy"><CountUp value={analytics.attemptCount} /></p>
        </div>
        <div className="min-w-0 rounded-xl bg-background-light p-3">
          <p className="eyebrow text-muted">Avg Score</p>
          <p className="mt-1 text-2xl font-bold tabular-nums text-navy">
            {analytics.averageScore !== null && analytics.averageScore !== undefined
              ? analytics.averageScore.toFixed(1)
              : '-'}
          </p>
        </div>
      </div>

      {analytics.learners && analytics.learners.length > 0 && (
        <div>
          <p className="mb-2 eyebrow text-muted">
            Per-learner breakdown
          </p>
          {/* Wide table: scrolls inside its frame on a phone. */}
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full min-w-[360px] text-start text-sm">
              <thead>
                <tr className="border-b border-border bg-background-light">
                  <th className="py-2 ps-3 pe-3 text-start eyebrow text-muted">
                    Learner
                  </th>
                  <th className="py-2 pe-3 text-end eyebrow text-muted">
                    Attempts
                  </th>
                  <th className="py-2 pe-3 text-end eyebrow text-muted">
                    Avg Score
                  </th>
                </tr>
              </thead>
              <tbody>
                {analytics.learners.map((l) => (
                  <tr key={l.userId} className="border-b border-border last:border-0">
                    <td className="py-2 ps-3 pe-3 text-sm text-navy">
                      {l.displayName ?? <em className="text-muted">Anonymous</em>}
                    </td>
                    <td className="py-2 pe-3 text-end text-sm tabular-nums text-navy">{l.attemptCount}</td>
                    <td className="py-2 pe-3 text-end text-sm tabular-nums text-navy">
                      {l.averageScore !== null && l.averageScore !== undefined
                        ? l.averageScore.toFixed(1)
                        : '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Class card ──────────────────────────────────────────────────────────────

interface ClassCardProps {
  cls: TeacherClass;
}

function ClassCard({ cls }: ClassCardProps) {
  const [analyticsOpen, setAnalyticsOpen] = useState(false);
  const [analytics, setAnalytics] = useState<ClassAnalytics | null>(null);
  const [analyticsLoading, setAnalyticsLoading] = useState(false);
  const [analyticsError, setAnalyticsError] = useState<string | null>(null);

  const loadAnalytics = async () => {
    if (analytics) {
      setAnalyticsOpen((v) => !v);
      return;
    }
    setAnalyticsOpen(true);
    setAnalyticsLoading(true);
    setAnalyticsError(null);
    try {
      const data = await apiClient.get<ClassAnalytics>(
        `/v1/listening/v2/teacher/classes/${encodeURIComponent(cls.id)}/analytics`,
      );
      setAnalytics(data);
    } catch (e) {
      setAnalyticsError(e instanceof Error ? e.message : 'Failed to load analytics.');
    } finally {
      setAnalyticsLoading(false);
    }
  };

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            {/* h2: the class cards sit straight under the page h1 (CardTitle is an h3). */}
            <h2 className="text-lg font-bold text-navy">{cls.name}</h2>
            {cls.description && (
              <p className="text-sm text-muted">{cls.description}</p>
            )}
          </div>
          {cls.memberCount !== undefined && (
            <Badge variant="muted" className="shrink-0 gap-1 tabular-nums">
              <Users className="h-3 w-3" aria-hidden />
              {cls.memberCount} {cls.memberCount === 1 ? 'member' : 'members'}
            </Badge>
          )}
        </div>
      </CardHeader>
      <CardContent>
        <Button
          variant="outline"
          size="sm"
          onClick={loadAnalytics}
          aria-expanded={analyticsOpen}
        >
          <BarChart2 className="h-4 w-4" aria-hidden />
          {analyticsOpen ? (
            <>
              Hide Analytics <ChevronUp className="h-4 w-4" aria-hidden />
            </>
          ) : (
            <>
              View Analytics <ChevronDown className="h-4 w-4" aria-hidden />
            </>
          )}
        </Button>

        {analyticsOpen && (
          <AnalyticsPanel
            analytics={analytics}
            loading={analyticsLoading}
            error={analyticsError}
          />
        )}
      </CardContent>
    </Card>
  );
}

// ─── Page ────────────────────────────────────────────────────────────────────

type PageState = 'loading' | 'ready' | 'error' | 'unauthorized';

export default function ListeningTeacherClassesPage() {
  const { role, isAuthenticated, isLoading } = useCurrentUser();

  const [pageState, setPageState] = useState<PageState>('loading');
  const [classes, setClasses] = useState<TeacherClass[]>([]);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const isTeachingStaff = role === 'expert' || role === 'admin';

  const loadClasses = useCallback(async () => {
    setPageState('loading');
    setFetchError(null);
    try {
      const data = await teacherClassApi.list();
      setClasses(Array.isArray(data) ? data : []);
      setPageState('ready');
    } catch (e) {
      setFetchError(e instanceof Error ? e.message : 'Could not load classes.');
      setPageState('error');
    }
  }, []);

  useEffect(() => {
    if (isLoading) return;
    if (!isAuthenticated) {
      setPageState('unauthorized');
      return;
    }
    if (!isTeachingStaff) {
      setPageState('unauthorized');
      return;
    }
    void loadClasses();
  }, [isLoading, isAuthenticated, isTeachingStaff, loadClasses]);

  const handleCreateClass = async (name: string, description: string) => {
    await teacherClassApi.create(name, description);
    await loadClasses();
  };

  // The breadcrumb's "Listening" crumb is the way back, so no back button above the header.
  return (
    <>
      <LearnerPageHero
        icon={Users}
        accent="listening"
        title="Teacher Class Analytics"
        description="View class progress and learner breakdowns."
        aside={pageState === 'ready' ? (
          <Button variant="primary" onClick={() => setShowCreate(true)}>
            <Plus className="h-4 w-4" aria-hidden />
            Create Class
          </Button>
        ) : undefined}
      />

      {/* Content */}
      {(isLoading || pageState === 'loading') && (
        <div className="space-y-4">
          <Skeleton className="h-32 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      )}

      {pageState === 'unauthorized' && (
        <InlineAlert variant="error">
          This page is only accessible to teaching staff (expert or admin role).
        </InlineAlert>
      )}

      {pageState === 'error' && fetchError && (
        <ErrorState message={fetchError} onRetry={() => void loadClasses()} retryLabel="Retry" />
      )}

      {pageState === 'ready' && classes.length === 0 && (
        <EmptyState
          icon={<Users className="h-8 w-8" aria-hidden />}
          title="No classes yet"
          description="Create your first class to start tracking learner progress."
          action={{ label: 'Create Class', onClick: () => setShowCreate(true) }}
        />
      )}

      {pageState === 'ready' && classes.length > 0 && (
        <ul className="space-y-4">
          {classes.map((cls, index) => (
            <li key={cls.id}>
              <MotionItem delayIndex={Math.min(index, 5)}>
                <ClassCard cls={cls} />
              </MotionItem>
            </li>
          ))}
        </ul>
      )}

      <CreateClassDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        onCreate={handleCreateClass}
      />
    </>
  );
}
