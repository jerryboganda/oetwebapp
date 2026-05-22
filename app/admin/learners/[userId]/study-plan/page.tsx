'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, RefreshCw, CheckCircle2, Clock, AlertTriangle, Calendar } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { forceRegenerateLearnerStudyPlan } from '@/lib/study-plan-admin-api';

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
  const router = useRouter();

  const [plan, setPlan] = useState<AdminStudyPlan | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [regenLoading, setRegenLoading] = useState(false);
  const [regenError, setRegenError] = useState<string | null>(null);
  const [regenSuccess, setRegenSuccess] = useState(false);
  const [filter, setFilter] = useState<'all' | 'today' | 'overdue' | 'completed'>('all');

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

  return (
    <div className="max-w-6xl mx-auto p-6 space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-3">
          <button
            onClick={() => router.back()}
            className="p-2 rounded-lg hover:bg-muted transition-colors"
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
          <div>
            <h1 className="text-2xl font-bold">Learner Study Plan</h1>
            <p className="text-sm text-muted-foreground font-mono">{userId}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={load}
            disabled={loading}
            className="inline-flex items-center gap-2 px-3 py-2 rounded-lg border text-sm hover:bg-muted transition-colors disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
          <button
            onClick={handleForceRegen}
            disabled={regenLoading || loading}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 disabled:opacity-50 transition-colors"
          >
            <RefreshCw className={`w-4 h-4 ${regenLoading ? 'animate-spin' : ''}`} />
            {regenLoading ? 'Queuing…' : 'Force Regenerate'}
          </button>
        </div>
      </div>

      {regenError && (
        <div className="p-3 rounded-lg bg-danger/10 text-danger text-sm">{regenError}</div>
      )}
      {regenSuccess && (
        <div className="p-3 rounded-lg bg-success/10 text-success text-sm flex items-center gap-2">
          <CheckCircle2 className="w-4 h-4" />
          Regeneration job queued — plan will refresh shortly.
        </div>
      )}

      {loading && (
        <div className="text-center py-12 text-muted-foreground">Loading plan…</div>
      )}

      {error && !loading && (
        <div className="text-center py-12 text-danger">{error}</div>
      )}

      {!loading && !error && !plan && (
        <div className="text-center py-12 text-muted-foreground">
          <Calendar className="w-8 h-8 mx-auto mb-3 opacity-50" />
          <p>No active study plan for this learner.</p>
          <button
            onClick={handleForceRegen}
            disabled={regenLoading}
            className="mt-4 inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 ${regenLoading ? 'animate-spin' : ''}`} />
            Generate Plan
          </button>
        </div>
      )}

      {plan && !loading && (
        <>
          {/* Plan metadata */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <MetaCard label="Plan Version" value={`v${plan.version}`} />
            <MetaCard label="Tier" value={plan.entitlementTierAtGeneration} />
            <MetaCard label="Week" value={`${plan.weekNumber} / ${plan.totalWeeks}`} />
            <MetaCard
              label="Window"
              value={`${plan.planWindowStart} → ${plan.planWindowEnd}`}
            />
          </div>

          {plan.templateId && (
            <div className="text-sm text-muted-foreground">
              Template:{' '}
              <Link
                href={`/admin/study-plan-templates/${plan.templateId}`}
                className="underline hover:no-underline font-mono"
              >
                {plan.templateId}
              </Link>
              {plan.isPremiumPersonalized && (
                <span className="ml-2 px-2 py-0.5 rounded-full bg-primary/10 text-primary text-xs font-medium">
                  AI Personalized
                </span>
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
                color="text-success"
              />
              <StatChip
                icon={<Clock className="w-4 h-4" />}
                label="In Progress"
                value={stats.inProgress}
                color="text-primary"
              />
              <StatChip
                icon={<AlertTriangle className="w-4 h-4" />}
                label="Overdue"
                value={stats.overdue}
                color="text-danger"
              />
              <StatChip
                icon={<Calendar className="w-4 h-4" />}
                label="Total"
                value={stats.total}
                color="text-muted-foreground"
              />
            </div>
          )}

          {/* Filter tabs */}
          <div className="flex gap-1 border-b">
            {(['all', 'today', 'overdue', 'completed'] as const).map((f) => (
              <button
                key={f}
                onClick={() => setFilter(f)}
                className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors capitalize ${
                  filter === f
                    ? 'border-primary text-primary'
                    : 'border-transparent text-muted-foreground hover:text-foreground'
                }`}
              >
                {f}
              </button>
            ))}
          </div>

          {/* Items table */}
          <div className="overflow-x-auto rounded-xl border">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-muted/50 text-left">
                  <th className="px-4 py-3 font-medium">Task</th>
                  <th className="px-4 py-3 font-medium">Subtest</th>
                  <th className="px-4 py-3 font-medium">Section</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 font-medium">Week</th>
                  <th className="px-4 py-3 font-medium">Due</th>
                  <th className="px-4 py-3 font-medium">Min</th>
                  <th className="px-4 py-3 font-medium">Feedback</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {filteredItems.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="px-4 py-8 text-center text-muted-foreground">
                      No items match this filter.
                    </td>
                  </tr>
                ) : (
                  filteredItems.map((item) => (
                    <tr key={item.id} className="hover:bg-muted/30 transition-colors">
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
                          : '—'}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

function MetaCard({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="border rounded-xl p-4">
      <p className="text-xs text-muted-foreground uppercase tracking-wide">{label}</p>
      <p className="font-semibold mt-1">{value}</p>
    </div>
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
      <span className="text-muted-foreground">{label}</span>
    </div>
  );
}
