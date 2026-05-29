'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import {
  ArrowLeft,
  BarChart2,
  ChevronDown,
  ChevronUp,
  Loader2,
  Plus,
  Users,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import { fetchWithTimeout } from '@/lib/network/fetch-with-timeout';

// ─── API helpers ────────────────────────────────────────────────────────────

const CSRF_SAFE = new Set(['GET', 'HEAD', 'OPTIONS']);

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl ?? '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

function readCsrf(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? decodeURIComponent(m[1]) : null;
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await ensureFreshAccessToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  const method = (init?.method ?? 'GET').toUpperCase();
  if (!CSRF_SAFE.has(method)) {
    const csrf = readCsrf();
    if (csrf) headers.set('x-csrf-token', csrf);
  }
  const res = await fetchWithTimeout(resolveUrl(path), {
    ...init,
    headers,
    credentials: init?.credentials ?? 'include',
  });
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      msg = body.message ?? body.title ?? msg;
    } catch { /* ignore */ }
    const err = new Error(msg) as Error & { status: number };
    err.status = res.status;
    throw err;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

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
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
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
        <div className="rounded-xl bg-background-light p-3">
          <p className="text-xs font-semibold uppercase tracking-wider text-muted">
            Attempts (last {analytics.days}d)
          </p>
          <p className="mt-1 text-2xl font-bold text-navy">{analytics.attemptCount}</p>
        </div>
        <div className="rounded-xl bg-background-light p-3">
          <p className="text-xs font-semibold uppercase tracking-wider text-muted">Avg Score</p>
          <p className="mt-1 text-2xl font-bold text-navy">
            {analytics.averageScore !== null && analytics.averageScore !== undefined
              ? analytics.averageScore.toFixed(1)
              : '—'}
          </p>
        </div>
      </div>

      {analytics.learners && analytics.learners.length > 0 && (
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted">
            Per-learner breakdown
          </p>
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full min-w-[360px] text-left text-sm">
              <thead>
                <tr className="border-b border-border bg-background-light">
                  <th className="py-2 pl-3 pr-3 text-xs font-semibold uppercase tracking-wider text-muted">
                    Learner
                  </th>
                  <th className="py-2 pr-3 text-xs font-semibold uppercase tracking-wider text-muted">
                    Attempts
                  </th>
                  <th className="py-2 pr-3 text-xs font-semibold uppercase tracking-wider text-muted">
                    Avg Score
                  </th>
                </tr>
              </thead>
              <tbody>
                {analytics.learners.map((l) => (
                  <tr key={l.userId} className="border-b border-border last:border-0">
                    <td className="py-2 pl-3 pr-3 text-sm text-navy">
                      {l.displayName ?? <em className="text-muted">Anonymous</em>}
                    </td>
                    <td className="py-2 pr-3 text-sm text-navy">{l.attemptCount}</td>
                    <td className="py-2 pr-3 text-sm text-navy">
                      {l.averageScore !== null && l.averageScore !== undefined
                        ? l.averageScore.toFixed(1)
                        : '—'}
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
      const data = await apiFetch<ClassAnalytics>(
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
          <div className="space-y-1">
            <CardTitle>{cls.name}</CardTitle>
            {cls.description && (
              <p className="text-sm text-muted">{cls.description}</p>
            )}
          </div>
          {cls.memberCount !== undefined && (
            <Badge variant="info">
              <Users className="mr-1 h-3 w-3" />
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
        >
          <BarChart2 className="h-4 w-4" />
          {analyticsOpen ? (
            <>
              Hide Analytics <ChevronUp className="h-4 w-4" />
            </>
          ) : (
            <>
              View Analytics <ChevronDown className="h-4 w-4" />
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
      const data = await apiFetch<TeacherClass[]>('/v1/listening/v2/teacher/classes');
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
    await apiFetch<TeacherClass>('/v1/listening/v2/teacher/classes', {
      method: 'POST',
      body: JSON.stringify({ name, description }),
    });
    await loadClasses();
  };

  return (
    <LearnerDashboardShell
      pageTitle="Class Analytics"
      subtitle="Manage your classes and view learner progress."
      backHref="/listening"
    >
      <div className="space-y-6 pb-24">
        {/* Back nav */}
        <Button variant="ghost" size="sm" className="gap-2 -ml-2" asChild>
          <Link href="/listening">
            <ArrowLeft className="h-4 w-4" />
            Listening home
          </Link>
        </Button>

        {/* Header */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 className="text-2xl font-bold text-navy">Teacher Class Analytics</h1>
            <p className="mt-1 text-sm text-muted">
              View class progress and learner breakdowns.
            </p>
          </div>
          {pageState === 'ready' && (
            <Button variant="primary" onClick={() => setShowCreate(true)}>
              <Plus className="h-4 w-4" />
              Create Class
            </Button>
          )}
        </div>

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
          <div className="space-y-3">
            <InlineAlert variant="error">{fetchError}</InlineAlert>
            <Button variant="outline" onClick={loadClasses}>
              Retry
            </Button>
          </div>
        )}

        {pageState === 'ready' && classes.length === 0 && (
          <Card>
            <CardContent>
              <div className="py-8 text-center">
                <Users className="mx-auto mb-3 h-10 w-10 text-muted" />
                <p className="font-semibold text-navy">No classes yet</p>
                <p className="mt-1 text-sm text-muted">
                  Create your first class to start tracking learner progress.
                </p>
                <Button
                  variant="primary"
                  className="mt-4"
                  onClick={() => setShowCreate(true)}
                >
                  <Plus className="h-4 w-4" />
                  Create Class
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {pageState === 'ready' && classes.length > 0 && (
          <div className="space-y-4">
            {classes.map((cls) => (
              <ClassCard key={cls.id} cls={cls} />
            ))}
          </div>
        )}
      </div>

      <CreateClassDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        onCreate={handleCreateClass}
      />
    </LearnerDashboardShell>
  );
}
