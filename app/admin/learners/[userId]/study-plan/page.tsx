'use client';

import { Fragment, useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { RefreshCw, CheckCircle2, Clock, AlertTriangle, Calendar, Pencil, X } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { forceRegenerateLearnerStudyPlan, overrideLearnerStudyPlanItem } from '@/lib/study-plan-admin-api';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';

interface AdminStudyPlanItem {
  id: string;
  title: string;
  description: string;
  subtestCode: string;
  section: string;
  status: string;
  durationMinutes: number;
  priorityScore: number;
  weekIndex: number;
  dueDate: string;
  completedAt: string | null;
  contentRoute: string | null;
  slotKind: string;
  feedbackRating: number | null;
  rationale: string | null;
  linkedReviewItemId: string | null;
  replacedById: string | null;
  contentId: string | null;
  itemType: string;
}

interface StudyPlanItemDraft {
  title: string;
  rationale: string;
  dueDate: string;
  durationMinutes: string;
  contentRoute: string;
  section: string;
}

interface AdminStudyPlan {
  planId: string;
  version: number;
  state: string;
  templateId: string | null;
  totalWeeks: number;
  weekNumber: number;
  minutesPerDayBudget: number;
  entitlementTierAtGeneration: string;
  isPremiumPersonalized: boolean;
  planWindowStart: string;
  planWindowEnd: string;
  generatedAt: string;
  items: AdminStudyPlanItem[];
}

const STATUS_BADGE: Record<string, string> = {
  Completed: 'bg-success/10 text-success',
  InProgress: 'bg-primary/10 text-primary',
  NotStarted: 'bg-muted text-muted-foreground',
  Skipped: 'bg-warning/10 text-warning',
  Overdue: 'bg-danger/10 text-danger',
};

const SUBTEST_COLOR: Record<string, string> = {
  reading: 'bg-blue-100 text-blue-800',
  listening: 'bg-purple-100 text-purple-800',
  writing: 'bg-green-100 text-green-800',
  speaking: 'bg-orange-100 text-orange-800',
  vocabulary: 'bg-yellow-100 text-yellow-800',
  pronunciation: 'bg-pink-100 text-pink-800',
  mock: 'bg-red-100 text-red-800',
};

export default function AdminLearnerStudyPlanPage() {
  const params = useParams();
  const userId =
    params && typeof params.userId === 'string'
      ? params.userId
      : Array.isArray(params?.userId)
      ? params.userId[0]
      : '';

  const [plan, setPlan] = useState<AdminStudyPlan | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenLoading, setRegenLoading] = useState(false);
  const [regenError, setRegenError] = useState<string | null>(null);
  const [regenSuccess, setRegenSuccess] = useState(false);
  const [filter, setFilter] = useState<'all' | 'today' | 'overdue' | 'completed'>('all');
  const [editingItemId, setEditingItemId] = useState<string | null>(null);
  const [draft, setDraft] = useState<StudyPlanItemDraft | null>(null);
  const [savingItemId, setSavingItemId] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const load = async () => {
    if (!userId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.get<AdminStudyPlan>(`/v1/admin/study-plan/${userId}`);
      setPlan(data);
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setError(err.userMessage ?? err.message ?? 'Failed to load study plan');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, [userId]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleForceRegen = async () => {
    setRegenLoading(true);
    setRegenError(null);
    setRegenSuccess(false);
    try {
      await forceRegenerateLearnerStudyPlan(userId);
      setRegenSuccess(true);
      setTimeout(() => {
        setRegenSuccess(false);
        load();
      }, 2000);
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setRegenError(err.userMessage ?? err.message ?? 'Regeneration failed');
    } finally {
      setRegenLoading(false);
    }
  };

  const beginEdit = (item: AdminStudyPlanItem) => {
    setSaveError(null);
    setEditingItemId(item.id);
    setDraft({
      title: item.title,
      rationale: item.rationale ?? '',
      dueDate: item.dueDate,
      durationMinutes: String(item.durationMinutes),
      contentRoute: item.contentRoute ?? '',
      section: item.section,
    });
  };

  const cancelEdit = () => {
    setEditingItemId(null);
    setDraft(null);
    setSaveError(null);
  };

  const saveItem = async (itemId: string) => {
    if (!draft || !draft.title.trim() || !draft.dueDate) return;
    setSavingItemId(itemId);
    setSaveError(null);
    try {
      await overrideLearnerStudyPlanItem(userId, itemId, {
        title: draft.title.trim(),
        rationale: draft.rationale.trim() || undefined,
        dueDate: draft.dueDate,
        durationMinutes: Math.max(1, Number(draft.durationMinutes) || 1),
        contentRoute: draft.contentRoute.trim() || undefined,
        section: draft.section.trim() || undefined,
      });
      cancelEdit();
      await load();
    } catch (e: unknown) {
      const err = e as { userMessage?: string; message?: string };
      setSaveError(err.userMessage ?? err.message ?? 'Unable to save this recommendation.');
    } finally {
      setSavingItemId(null);
    }
  };

  const filteredItems = plan?.items.filter((item) => {
    if (filter === 'all') return true;
    if (filter === 'today') return item.section === 'Today';
    if (filter === 'overdue') return item.status === 'Overdue';
    if (filter === 'completed') return item.status === 'Completed';
    return true;
  }) ?? [];

  const stats = plan
    ? {
        total: plan.items.length,
        completed: plan.items.filter((i) => i.status === 'Completed').length,
        overdue: plan.items.filter((i) => i.status === 'Overdue').length,
        inProgress: plan.items.filter((i) => i.status === 'InProgress').length,
      }
    : null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Learners', href: '/admin/users' },
    { label: userId },
    { label: 'Study plan' },
  ];

  return (
    <AdminOperationsLayout
      title="Learner Study Plan"
      description={userId}
      breadcrumbs={breadcrumbs}
      eyebrow="Learners"
      icon={<Calendar className="h-5 w-5" />}
      actions={(
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={load} disabled={loading}>
            <RefreshCw className={`w-4 h-4 mr-1 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
          <Button onClick={handleForceRegen} disabled={regenLoading || loading} loading={regenLoading}>
            <RefreshCw className="w-4 h-4 mr-1" />
            {regenLoading ? 'Queuing…' : 'Force Regenerate'}
          </Button>
        </div>
      )}
      kpis={plan && stats ? (
        <KpiStrip className="lg:grid-cols-4">
          <KpiTile label="Plan Version" value={`v${plan.version}`} tone="primary" />
          <KpiTile label="Tier" value={plan.entitlementTierAtGeneration} />
          <KpiTile label="Week" value={`${plan.weekNumber} / ${plan.totalWeeks}`} />
          <KpiTile label="Total tasks" value={stats.total} />
        </KpiStrip>
      ) : undefined}
    >
      {regenError && (
        <Card surface="tinted-danger">
          <CardContent className="p-3 text-sm text-admin-danger">{regenError}</CardContent>
        </Card>
      )}
      {saveError && (
        <Card surface="tinted-danger">
          <CardContent className="p-3 text-sm text-admin-danger" role="alert">{saveError}</CardContent>
        </Card>
      )}
      {regenSuccess && (
        <Card surface="tinted-success">
          <CardContent className="p-3 text-sm text-admin-success flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4" />
            Regeneration job queued. Plan will refresh shortly.
          </CardContent>
        </Card>
      )}

      {loading && (
        <div className="text-center py-12 text-admin-fg-muted">Loading plan…</div>
      )}

      {error && !loading && (
        <div className="text-center py-12 text-admin-danger">{error}</div>
      )}

      {!loading && !error && !plan && (
        <div className="text-center py-12 text-admin-fg-muted">
          <Calendar className="w-8 h-8 mx-auto mb-3 opacity-50" />
          <p>No active study plan for this learner.</p>
          <Button className="mt-4" onClick={handleForceRegen} disabled={regenLoading} loading={regenLoading}>
            <RefreshCw className="w-4 h-4 mr-1" />
            Generate Plan
          </Button>
        </div>
      )}

      {plan && !loading && (
        <>
          {plan.templateId && (
            <div className="text-sm text-admin-fg-muted">
              Template:{' '}
              <Link
                href={`/admin/study-plan-templates/${plan.templateId}`}
                className="underline hover:no-underline font-mono text-[var(--admin-primary)]"
              >
                {plan.templateId}
              </Link>
              {plan.isPremiumPersonalized && (
                <Badge variant="primary" className="ml-2">AI Personalized</Badge>
              )}
            </div>
          )}

          {/* Stats row */}
          {stats && (
            <div className="flex gap-4 flex-wrap">
              <StatChip
                icon={<CheckCircle2 className="w-4 h-4" />}
                label="Completed"
                value={stats.completed}
                color="text-admin-success"
              />
              <StatChip
                icon={<Clock className="w-4 h-4" />}
                label="In Progress"
                value={stats.inProgress}
                color="text-[var(--admin-primary)]"
              />
              <StatChip
                icon={<AlertTriangle className="w-4 h-4" />}
                label="Overdue"
                value={stats.overdue}
                color="text-admin-danger"
              />
              <StatChip
                icon={<Calendar className="w-4 h-4" />}
                label="Total"
                value={stats.total}
                color="text-admin-fg-muted"
              />
            </div>
          )}

          {/* Filter tabs */}
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Filter</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="flex gap-1 border-b border-admin-border">
                {(['all', 'today', 'overdue', 'completed'] as const).map((f) => (
                  <button
                    key={f}
                    onClick={() => setFilter(f)}
                    className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors capitalize ${
                      filter === f
                        ? 'border-[var(--admin-primary)] text-[var(--admin-primary)]'
                        : 'border-transparent text-admin-fg-muted hover:text-admin-fg-strong'
                    }`}
                  >
                    {f}
                  </button>
                ))}
              </div>
            </CardContent>
          </Card>

          {/* Items table */}
          <div className="overflow-x-auto rounded-admin-lg border border-admin-border bg-admin-bg-surface">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-muted/50 text-left">
                  <th scope="col" className="px-4 py-3 font-medium">Task</th>
                  <th scope="col" className="px-4 py-3 font-medium">Subtest</th>
                  <th scope="col" className="px-4 py-3 font-medium">Section</th>
                  <th scope="col" className="px-4 py-3 font-medium">Status</th>
                  <th scope="col" className="px-4 py-3 font-medium">Week</th>
                  <th scope="col" className="px-4 py-3 font-medium">Due</th>
                  <th scope="col" className="px-4 py-3 font-medium">Min</th>
                  <th scope="col" className="px-4 py-3 font-medium">Feedback</th>
                  <th scope="col" className="px-4 py-3 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {filteredItems.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-4 py-8 text-center text-muted-foreground">
                      No items match this filter.
                    </td>
                  </tr>
                ) : (
                  filteredItems.map((item) => (
                    <Fragment key={item.id}>
                    <tr className="hover:bg-muted/30 transition-colors">
                      <td className="px-4 py-3 max-w-xs">
                        <p className="font-medium truncate">{item.title}</p>
                        {item.rationale && (
                          <p className="text-xs text-muted-foreground truncate mt-0.5">
                            {item.rationale}
                          </p>
                        )}
                        {item.contentRoute && (
                          <Link
                            href={item.contentRoute}
                            className="text-xs text-primary underline hover:no-underline"
                            target="_blank"
                          >
                            {item.contentRoute}
                          </Link>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                            SUBTEST_COLOR[item.subtestCode] ?? 'bg-muted text-muted-foreground'
                          }`}
                        >
                          {item.subtestCode}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground capitalize">
                        {item.section.replace(/-/g, ' ')}
                      </td>
                      <td className="px-4 py-3">
                        <span
                          className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                            STATUS_BADGE[item.status] ?? 'bg-muted text-muted-foreground'
                          }`}
                        >
                          {item.status}
                        </span>
                        {item.replacedById && (
                          <span className="ml-1 text-xs text-muted-foreground">(replaced)</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-center text-muted-foreground">
                        {item.weekIndex + 1}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground text-xs">{item.dueDate}</td>
                      <td className="px-4 py-3 text-center text-muted-foreground">
                        {item.durationMinutes}
                      </td>
                      <td className="px-4 py-3 text-center text-muted-foreground">
                        {item.feedbackRating === 1
                          ? '😊 Easy'
                          : item.feedbackRating === 2
                          ? '✅ OK'
                          : item.feedbackRating === 3
                          ? '😓 Hard'
                          : '-'}
                      </td>
                      <td className="px-4 py-3">
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => editingItemId === item.id ? cancelEdit() : beginEdit(item)}
                          aria-expanded={editingItemId === item.id}
                          aria-controls={`study-plan-edit-${item.id}`}
                        >
                          {editingItemId === item.id ? <X className="h-3.5 w-3.5" aria-hidden /> : <Pencil className="h-3.5 w-3.5" aria-hidden />}
                          {editingItemId === item.id ? 'Close' : 'Edit'}
                        </Button>
                      </td>
                    </tr>
                    {editingItemId === item.id && draft ? (
                      <tr id={`study-plan-edit-${item.id}`} className="bg-admin-bg-subtle">
                        <td colSpan={9} className="px-4 py-4">
                          <form
                            className="grid gap-4 md:grid-cols-2"
                            onSubmit={(event) => { event.preventDefault(); void saveItem(item.id); }}
                          >
                            <Input
                              label="Recommendation title"
                              value={draft.title}
                              onChange={(event) => setDraft({ ...draft, title: event.target.value })}
                              required
                            />
                            <Input
                              label="Due date"
                              type="date"
                              value={draft.dueDate}
                              onChange={(event) => setDraft({ ...draft, dueDate: event.target.value })}
                              required
                            />
                            <Input
                              label="Duration (minutes)"
                              type="number"
                              min={1}
                              value={draft.durationMinutes}
                              onChange={(event) => setDraft({ ...draft, durationMinutes: event.target.value })}
                              required
                            />
                            <Input
                              label="Practice route"
                              value={draft.contentRoute}
                              onChange={(event) => setDraft({ ...draft, contentRoute: event.target.value })}
                              hint="Use an internal learner route, such as /reading/practice."
                            />
                            <Input
                              label="Plan section"
                              value={draft.section}
                              onChange={(event) => setDraft({ ...draft, section: event.target.value })}
                            />
                            <Textarea
                              label="Rationale"
                              value={draft.rationale}
                              onChange={(event) => setDraft({ ...draft, rationale: event.target.value })}
                              rows={3}
                              wrapperClassName="md:col-span-2"
                            />
                            <div className="flex items-center gap-2 md:col-span-2">
                              <Button type="submit" size="sm" loading={savingItemId === item.id} disabled={savingItemId !== null}>
                                Save recommendation
                              </Button>
                              <Button type="button" variant="ghost" size="sm" onClick={cancelEdit} disabled={savingItemId !== null}>
                                Cancel
                              </Button>
                              <span className="text-xs text-admin-fg-muted">Changes are audit-logged.</span>
                            </div>
                          </form>
                        </td>
                      </tr>
                    ) : null}
                    </Fragment>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </AdminOperationsLayout>
  );
}

function StatChip({
  icon,
  label,
  value,
  color,
}: {
  icon: ReactNode;
  label: string;
  value: number;
  color: string;
}) {
  return (
    <div className={`flex items-center gap-1.5 text-sm ${color}`}>
      {icon}
      <span className="font-medium">{value}</span>
      <span className="text-admin-fg-muted">{label}</span>
    </div>
  );
}
