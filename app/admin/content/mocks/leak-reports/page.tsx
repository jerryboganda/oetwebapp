'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, ArrowLeft, ShieldAlert } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { Modal } from '@/components/ui/modal';
import {
  listAdminMockLeakReports,
  updateAdminMockLeakReport,
  type AdminMockLeakReport,
  type AdminMockLeakReportStatus,
} from '@/lib/api';

type StatusFilter = '' | AdminMockLeakReportStatus;

const STATUS_FILTERS: { value: StatusFilter; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'open', label: 'Open' },
  { value: 'investigating', label: 'Investigating' },
  { value: 'resolved', label: 'Resolved' },
  { value: 'dismissed', label: 'Dismissed' },
];

const STATUS_BADGE: Record<AdminMockLeakReportStatus, { label: string; variant: 'danger' | 'warning' | 'success' | 'muted' }> = {
  open: { label: 'Open', variant: 'danger' },
  investigating: { label: 'Investigating', variant: 'warning' },
  resolved: { label: 'Resolved', variant: 'success' },
  dismissed: { label: 'Dismissed', variant: 'muted' },
};

const SEVERITY_VARIANT: Record<string, 'danger' | 'warning' | 'muted'> = {
  high: 'danger',
  medium: 'warning',
  low: 'muted',
};

type ActionMode = 'investigating' | 'resolved' | 'dismissed';

const ACTION_LABELS: Record<ActionMode, string> = {
  investigating: 'Mark investigating',
  resolved: 'Mark resolved',
  dismissed: 'Dismiss report',
};

function formatDateTime(value: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

export default function AdminMockLeakReportsPage() {
  const [rows, setRows] = useState<AdminMockLeakReport[]>([]);
  const [filter, setFilter] = useState<StatusFilter>('open');
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [actionTarget, setActionTarget] = useState<AdminMockLeakReport | null>(null);
  const [actionMode, setActionMode] = useState<ActionMode | null>(null);
  const [actionNote, setActionNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await listAdminMockLeakReports({
        status: filter || undefined,
        limit: 50,
      });
      setRows(response.items ?? []);
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof Error ? err.message : 'Failed to load leak reports.',
      });
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => {
    void load();
  }, [load]);

  const counts = useMemo(() => {
    const out: Record<string, number> = { open: 0, investigating: 0, resolved: 0, dismissed: 0 };
    for (const row of rows) {
      out[row.status] = (out[row.status] ?? 0) + 1;
    }
    return out;
  }, [rows]);

  function openAction(row: AdminMockLeakReport, mode: ActionMode) {
    setActionTarget(row);
    setActionMode(mode);
    setActionNote(row.resolutionNote ?? '');
  }

  function closeAction() {
    setActionTarget(null);
    setActionMode(null);
    setActionNote('');
    setSubmitting(false);
  }

  async function handleSubmit() {
    if (!actionTarget || !actionMode) return;
    setSubmitting(true);
    try {
      await updateAdminMockLeakReport(actionTarget.id, {
        status: actionMode,
        resolutionNote: actionNote.trim() ? actionNote.trim() : undefined,
      });
      setToast({ variant: 'success', message: `Report marked ${actionMode}.` });
      closeAction();
      await load();
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof Error ? err.message : 'Update failed.',
      });
      setSubmitting(false);
    }
  }

  return (
    <>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Content integrity"
            title="Mock leak-report queue"
            description="Triage learner-submitted leak reports against published mock bundles. Every status change is recorded in the audit log."
            icon={ShieldAlert}
          />

          <div className="mb-4">
            <Link
              href="/admin/content/mocks"
              className="inline-flex items-center text-sm font-bold text-primary hover:underline"
            >
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to Mock Bundles
            </Link>
          </div>

          <div
            className="mb-6 flex flex-wrap gap-2"
            role="toolbar"
            aria-label="Filter leak reports by status"
          >
            {STATUS_FILTERS.map((option) => {
              const isActive = filter === option.value;
              const count =
                option.value === '' ? rows.length : counts[option.value] ?? 0;
              return (
                <Button
                  key={option.value || 'all'}
                  variant={isActive ? 'primary' : 'secondary'}
                  onClick={() => setFilter(option.value)}
                  aria-pressed={isActive}
                  aria-label={`${option.label} reports (${count})`}
                >
                  {option.label}
                  <span className="ml-2 rounded-full bg-white/30 px-2 py-0.5 text-xs">
                    {count}
                  </span>
                </Button>
              );
            })}
          </div>

          {/* V2 Medium #2 (May 2026 audit closure): screen-reader-only
              live region that announces the result count when filters
              change, so non-sighted operators learn how many rows the
              new filter loaded without needing to scan the table. */}
          <p
            className="sr-only"
            role="status"
            aria-live="polite"
            aria-atomic="true"
          >
            {loading
              ? 'Loading leak reports.'
              : `${rows.length} leak report${rows.length === 1 ? '' : 's'} loaded for the current filter.`}
          </p>

          {loading ? (
            <div className="space-y-3">
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </div>
          ) : rows.length === 0 ? (
            <EmptyState
              title="No leak reports"
              description="No reports match the current filter."
              icon={<AlertTriangle className="h-8 w-8" />}
            />
          ) : (
            <div className="overflow-x-auto rounded-2xl border border-border">
              <table
                className="min-w-full divide-y divide-border text-sm"
                aria-label="Mock leak reports queue"
              >
                <caption className="sr-only">
                  Mock leak reports queue. Each row is one learner-submitted report; columns include bundle, severity, reason, reporter, creation timestamp, status, and triage actions.
                </caption>
                <thead className="bg-background-light">
                  <tr className="text-left text-xs font-black uppercase tracking-widest text-muted">
                    <th className="px-4 py-3">Bundle</th>
                    <th className="px-4 py-3">Severity</th>
                    <th className="px-4 py-3">Reason</th>
                    <th className="px-4 py-3">Reporter</th>
                    <th className="px-4 py-3">Created</th>
                    <th className="px-4 py-3">Status</th>
                    <th className="px-4 py-3">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border bg-surface">
                  {rows.map((row) => {
                    const isTerminal = row.status === 'resolved' || row.status === 'dismissed';
                    const statusBadge = STATUS_BADGE[row.status];
                    return (
                      <tr key={row.id} className="align-top">
                        <td className="px-4 py-3">
                          {row.bundleId ? (
                            <Link
                              href={`/admin/content/mocks/${encodeURIComponent(row.bundleId)}/item-analysis`}
                              className="font-bold text-primary hover:underline"
                            >
                              {row.bundleTitle ?? row.bundleId}
                            </Link>
                          ) : (
                            <span className="text-muted">—</span>
                          )}
                          {row.attemptId ? (
                            <p className="text-xs text-muted">Attempt: {row.attemptId}</p>
                          ) : null}
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant={SEVERITY_VARIANT[row.severity] ?? 'muted'}>
                            {row.severity}
                          </Badge>
                        </td>
                        <td className="px-4 py-3">
                          <p className="font-medium text-navy">{row.reasonCode ?? '—'}</p>
                          {row.pageOrQuestion ? (
                            <p className="text-xs text-muted">@ {row.pageOrQuestion}</p>
                          ) : null}
                          {row.evidenceUrl ? (
                            <a
                              href={row.evidenceUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                              className="text-xs text-primary hover:underline"
                            >
                              Evidence link
                            </a>
                          ) : null}
                        </td>
                        <td className="px-4 py-3 text-navy">
                          {row.reportedByUserDisplayName ?? row.reportedByUserId ?? '—'}
                        </td>
                        <td className="px-4 py-3 text-muted">{formatDateTime(row.createdAt)}</td>
                        <td className="px-4 py-3">
                          <Badge variant={statusBadge.variant}>{statusBadge.label}</Badge>
                          {row.resolvedAt ? (
                            <p className="mt-1 text-xs text-muted">
                              {formatDateTime(row.resolvedAt)}
                            </p>
                          ) : null}
                        </td>
                        <td className="px-4 py-3">
                          <div
                            className="flex flex-wrap gap-2"
                            role="group"
                            aria-label={`Triage actions for report ${row.id}`}
                          >
                            <Button
                              variant="secondary"
                              onClick={() => openAction(row, 'investigating')}
                              disabled={isTerminal}
                              aria-label={`Mark report ${row.id} as investigating`}
                            >
                              Investigate
                            </Button>
                            <Button
                              variant="primary"
                              onClick={() => openAction(row, 'resolved')}
                              disabled={isTerminal}
                              aria-label={`Mark report ${row.id} as resolved`}
                            >
                              Resolve
                            </Button>
                            <Button
                              variant="secondary"
                              onClick={() => openAction(row, 'dismissed')}
                              disabled={isTerminal}
                              aria-label={`Dismiss report ${row.id}`}
                            >
                              Dismiss
                            </Button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      <Modal
        open={actionTarget !== null && actionMode !== null}
        onClose={closeAction}
        title={actionMode ? ACTION_LABELS[actionMode] : ''}
      >
        <div className="space-y-4">
          {actionTarget ? (
            <div className="rounded-xl border border-border bg-background-light p-3 text-sm">
              <p className="font-bold text-navy">
                {actionTarget.bundleTitle ?? actionTarget.bundleId ?? '—'}
              </p>
              <p className="text-xs text-muted">Reported by {actionTarget.reportedByUserDisplayName ?? actionTarget.reportedByUserId ?? 'unknown'}</p>
              {actionTarget.reasonCode ? (
                <p className="mt-1 text-xs">Reason: {actionTarget.reasonCode}</p>
              ) : null}
            </div>
          ) : null}
          <Textarea
            label="Resolution note"
            value={actionNote}
            onChange={(e) => setActionNote(e.target.value)}
            rows={4}
            placeholder="Add an internal note explaining the decision."
          />
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={closeAction} disabled={submitting}>
              Cancel
            </Button>
            <Button variant="primary" onClick={handleSubmit} disabled={submitting}>
              {submitting ? 'Saving…' : 'Confirm'}
            </Button>
          </div>
        </div>
      </Modal>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      ) : null}
    </>
  );
}
