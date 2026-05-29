'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { CheckCircle2, ClipboardList, ExternalLink, RefreshCw, Search } from 'lucide-react';
import { ExpertDashboardShell } from '@/components/layout/expert-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import {
  listReadingAssignments,
  type ReadingAssignmentDto,
} from '@/lib/reading-tutor-api';

const KIND_LABELS: Record<string, string> = {
  full: 'Full reading exam',
  retake: 'Full retake',
  part: 'Part practice',
  part_a: 'Part A practice',
  part_bc: 'Parts B+C practice',
  'part-practice': 'Part practice',
  drill: 'Targeted drill',
  learning: 'Learning mode',
  'mini-test': 'Mini-test',
  'error-bank': 'Error bank',
};

function formatDate(iso: string | null): string {
  if (!iso) return 'No due date';
  try {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
  } catch {
    return iso;
  }
}

function statusTone(status: string): string {
  switch (status.toLowerCase()) {
    case 'completed':
      return 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-200';
    case 'cancelled':
      return 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300';
    default:
      return 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-200';
  }
}

export default function ExpertReadingQueuePage() {
  const [assignments, setAssignments] = useState<ReadingAssignmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');

  const load = useCallback(async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    setError(null);
    try {
      const items = await listReadingAssignments('', 'expert');
      setAssignments(items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load Reading assignments.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase();
    if (!needle) return assignments;
    return assignments.filter((assignment) =>
      assignment.assignedToUserId.toLowerCase().includes(needle)
      || assignment.paperId.toLowerCase().includes(needle)
      || assignment.kind.toLowerCase().includes(needle)
      || assignment.status.toLowerCase().includes(needle));
  }, [assignments, query]);

  const completedCount = assignments.filter((assignment) => assignment.status === 'completed').length;
  const reviewReadyCount = assignments.filter((assignment) => Boolean(assignment.completedAttemptId)).length;
  const openCount = assignments.filter((assignment) => assignment.status === 'assigned').length;

  return (
    <ExpertDashboardShell pageTitle="Reading queue" workspaceClassName="space-y-6">
      <header className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-widest text-primary">Reading tutor panel</p>
        <div className="flex flex-col gap-3 md:flex-row md:items-end md:justify-between">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-navy">Reading assignments</h1>
            <p className="mt-1 max-w-3xl text-sm text-muted">
              Track learners you assigned, open completed attempts for expert review, and keep intervention work moving from one queue.
            </p>
          </div>
          <button
            type="button"
            onClick={() => {
              setRefreshing(true);
              void load(false);
            }}
            disabled={refreshing}
            className="inline-flex items-center justify-center gap-2 rounded-lg border border-border bg-surface px-4 py-2 text-sm font-semibold text-navy transition-colors hover:bg-primary/5 disabled:opacity-60"
          >
            <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} aria-hidden />
            Refresh
          </button>
        </div>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <section className="grid gap-3 sm:grid-cols-3" aria-label="Reading queue summary">
        <SummaryTile label="Open assignments" value={openCount} />
        <SummaryTile label="Ready for review" value={reviewReadyCount} />
        <SummaryTile label="Completed" value={completedCount} />
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm" aria-label="Reading assignment queue">
        <div className="mb-4 flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <div>
            <h2 className="text-base font-bold text-navy">Assignment queue</h2>
            <p className="text-sm text-muted">Completed assignments link directly to the privileged Reading review screen.</p>
          </div>
          <label className="relative block md:w-80">
            <span className="sr-only">Search assignments</span>
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search learner, paper, status"
              className="w-full rounded-lg border border-border bg-background py-2 pl-9 pr-3 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
            />
          </label>
        </div>

        {loading ? (
          <p className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted">Loading Reading queue...</p>
        ) : filtered.length === 0 ? (
          <p className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted">No Reading assignments match this view.</p>
        ) : (
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/30 text-left text-xs font-semibold uppercase tracking-wide text-muted">
                  <th scope="col" className="px-4 py-3">Learner</th>
                  <th scope="col" className="px-4 py-3">Work</th>
                  <th scope="col" className="px-4 py-3">Status</th>
                  <th scope="col" className="px-4 py-3">Due</th>
                  <th scope="col" className="px-4 py-3 text-right">Review</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((assignment) => (
                  <tr key={assignment.id} className="border-b border-border last:border-b-0 hover:bg-primary/5">
                    <td className="px-4 py-3 font-medium text-navy">{assignment.assignedToUserId}</td>
                    <td className="px-4 py-3 text-muted">
                      <span className="block font-medium text-navy">{KIND_LABELS[assignment.kind] ?? assignment.kind}</span>
                      <span className="text-xs">Paper {assignment.paperId}</span>
                      {assignment.note ? <span className="mt-1 block text-xs">{assignment.note}</span> : null}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`rounded-full px-2 py-1 text-xs font-semibold capitalize ${statusTone(assignment.status)}`}>
                        {assignment.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-muted">{formatDate(assignment.dueAt)}</td>
                    <td className="px-4 py-3 text-right">
                      {assignment.completedAttemptId ? (
                        <Link
                          href={`/expert/reading/attempts/${assignment.completedAttemptId}`}
                          className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary-dark"
                        >
                          Open review
                          <ExternalLink className="h-3.5 w-3.5" aria-hidden />
                        </Link>
                      ) : (
                        <span className="text-xs text-muted">Awaiting submission</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </ExpertDashboardShell>
  );
}

function SummaryTile({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
      <p className="flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-muted">
        {label === 'Completed' ? <CheckCircle2 className="h-4 w-4" aria-hidden /> : <ClipboardList className="h-4 w-4" aria-hidden />}
        {label}
      </p>
      <p className="mt-2 text-3xl font-bold text-navy">{value}</p>
    </div>
  );
}
