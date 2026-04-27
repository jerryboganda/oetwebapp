'use client';

import { useEffect, useState } from 'react';
import { Scale, UserRoundCheck, CheckCircle2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { assignEscalationReviewer, resolveEscalation } from '@/lib/api';
import { getAdminEscalationsData, getAdminUsersPageData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminReviewEscalation, AdminUserRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const statusBadge: Record<string, { label: string; variant: 'default' | 'success' | 'danger' }> = {
  pending: { label: 'Pending', variant: 'default' },
  assigned: { label: 'Assigned', variant: 'default' },
  resolved: { label: 'Resolved', variant: 'success' },
};

export default function EscalationsPage() {
  useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
  const [escalations, setEscalations] = useState<AdminReviewEscalation[]>([]);
  const [experts, setExperts] = useState<AdminUserRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page] = useState(1);
  const [assignTarget, setAssignTarget] = useState<AdminReviewEscalation | null>(null);
  const [selectedExpertId, setSelectedExpertId] = useState('');
  const [resolveTarget, setResolveTarget] = useState<AdminReviewEscalation | null>(null);
  const [finalScore, setFinalScore] = useState('');
  const [resolutionNote, setResolutionNote] = useState('');
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const [escData, expertData] = await Promise.all([
          getAdminEscalationsData({ status: selectedStatus, page, pageSize: 20 }),
          getAdminUsersPageData({ role: 'expert', pageSize: 100 }),
        ]);
        if (cancelled) return;
        setEscalations(escData.items);
        setTotal(escData.total);
        setExperts(expertData.items);
        setPageStatus(escData.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [selectedStatus, page]);

  async function handleAssign() {
    if (!assignTarget || !selectedExpertId) return;
    setIsMutating(true);
    try {
      await assignEscalationReviewer(assignTarget.id, selectedExpertId);
      setToast({ variant: 'success', message: 'Expert assigned for second review.' });
      setAssignTarget(null);
      setSelectedExpertId('');
      setEscalations((prev) =>
        prev.map((e) => e.id === assignTarget.id ? { ...e, status: 'assigned' as const, secondReviewerId: selectedExpertId } : e),
      );
    } catch {
      setToast({ variant: 'error', message: 'Failed to assign reviewer.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleResolve() {
    if (!resolveTarget) return;
    const score = parseInt(finalScore, 10);
    if (isNaN(score) || score < 0 || score > 500) {
      setToast({ variant: 'error', message: 'Score must be between 0 and 500.' });
      return;
    }
    setIsMutating(true);
    try {
      await resolveEscalation(resolveTarget.id, score, resolutionNote || undefined);
      setToast({ variant: 'success', message: 'Escalation resolved.' });
      setResolveTarget(null);
      setFinalScore('');
      setResolutionNote('');
      setEscalations((prev) =>
        prev.map((e) => e.id === resolveTarget.id ? { ...e, status: 'resolved' as const, finalScore: score } : e),
      );
    } catch {
      setToast({ variant: 'error', message: 'Failed to resolve escalation.' });
    } finally {
      setIsMutating(false);
    }
  }

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'assigned', label: 'Assigned' },
        { id: 'resolved', label: 'Resolved' },
      ],
    },
  ];

  const columns: Column<AdminReviewEscalation>[] = [
    { key: 'subtestCode', header: 'Subtest', render: (e) => <Badge variant="muted">{e.subtestCode}</Badge> },
    { key: 'aiScore', header: 'AI Score', render: (e) => <span className="font-mono">{e.aiScore}</span> },
    { key: 'humanScore', header: 'Human Score', render: (e) => <span className="font-mono">{e.humanScore}</span> },
    {
      key: 'divergence',
      header: 'Divergence',
      render: (e) => (
        <span className={`font-mono font-semibold ${e.divergence >= 50 ? 'text-danger' : 'text-warning'}`}>
          {e.divergence}
        </span>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (e) => {
        const badge = statusBadge[e.status] ?? statusBadge.pending;
        return <Badge variant={badge.variant}>{badge.label}</Badge>;
      },
    },
    { key: 'finalScore', header: 'Final', render: (e) => e.finalScore != null ? <span className="font-mono font-semibold">{e.finalScore}</span> : <span className="text-muted">—</span> },
    {
      key: 'actions',
      header: '',
      render: (e) => (
        <div className="flex gap-1">
          {e.status === 'pending' && (
            <Button size="sm" variant="outline" onClick={() => { setAssignTarget(e); setSelectedExpertId(''); }}>
              <UserRoundCheck className="w-3.5 h-3.5 mr-1" />
              Assign
            </Button>
          )}
          {(e.status === 'pending' || e.status === 'assigned') && (
            <Button size="sm" variant="outline" onClick={() => { setResolveTarget(e); setFinalScore(''); setResolutionNote(''); }}>
              <CheckCircle2 className="w-3.5 h-3.5 mr-1" />
              Resolve
            </Button>
          )}
        </div>
      ),
    },
  ];

  const pendingCount = escalations.filter((e) => e.status === 'pending').length;

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminRouteSectionHeader
        title="Review Escalations"
        description="Manage cases where AI and human review scores significantly diverge."
      />

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
        <AdminRouteSummaryCard label="Total Escalations" value={total} />
        <AdminRouteSummaryCard label="Pending" value={pendingCount} tone={pendingCount > 0 ? 'warning' : 'default'} />
        <AdminRouteSummaryCard label="Resolved" value={escalations.filter((e) => e.status === 'resolved').length} tone="success" />
      </div>

      <FilterBar groups={filterGroups} selected={filters} onChange={(groupId, optionId) => setFilters(prev => { const arr = [...(prev[groupId] || [])]; const i = arr.indexOf(optionId); if (i >= 0) arr.splice(i, 1); else arr.push(optionId); return { ...prev, [groupId]: arr }; })} />

      <AsyncStateWrapper
        status={pageStatus}
        emptyContent={<EmptyState icon={<Scale className="w-12 h-12" />} title="No escalations" description="No review escalations found." />}
        errorMessage="Unable to load escalations."
      >
        <AdminRoutePanel>
          <DataTable
            columns={columns}
            data={escalations}
            keyExtractor={(e) => e.id}
            mobileCardRender={(e) => (
              <div className="space-y-1">
                <div className="flex gap-2">
                  <Badge variant="muted">{e.subtestCode}</Badge>
                  <Badge variant={statusBadge[e.status]?.variant ?? 'default'}>{statusBadge[e.status]?.label ?? e.status}</Badge>
                </div>
                <p className="text-xs">AI: {e.aiScore} | Human: {e.humanScore} | Div: {e.divergence}</p>
                {e.finalScore != null && <p className="text-xs font-semibold">Final: {e.finalScore}</p>}
              </div>
            )}
          />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {assignTarget && (
        <Modal
          open={!!assignTarget}
          onClose={() => setAssignTarget(null)}
          title="Assign Second Reviewer"
        >
          <div className="space-y-4 p-4">
            <p className="text-sm text-muted">
              Subtest: <Badge variant="muted">{assignTarget.subtestCode}</Badge>
              {' '}AI: {assignTarget.aiScore} | Human: {assignTarget.humanScore} (Divergence: {assignTarget.divergence})
            </p>
            <Select
              label="Select Expert Reviewer"
              value={selectedExpertId}
              onChange={(e) => setSelectedExpertId(e.target.value)}
              options={[{ value: '', label: 'Choose an expert...' }, ...experts.filter((ex) => ex.id !== assignTarget.originalReviewerId).map((ex) => ({ value: ex.id, label: `${ex.name} (${ex.email})` }))]}
            />
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-border dark:border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => setAssignTarget(null)}>Cancel</Button>
            <Button onClick={handleAssign} disabled={isMutating || !selectedExpertId}>
              {isMutating ? 'Assigning...' : 'Assign'}
            </Button>
          </div>
        </Modal>
      )}

      {resolveTarget && (
        <Modal
          open={!!resolveTarget}
          onClose={() => setResolveTarget(null)}
          title="Resolve Escalation"
        >
          <div className="space-y-4 p-4">
            <p className="text-sm text-muted">
              AI: {resolveTarget.aiScore} | Human: {resolveTarget.humanScore} (Divergence: {resolveTarget.divergence})
            </p>
            <Input
              label="Final Score (0–500)"
              type="number"
              min={0}
              max={500}
              value={finalScore}
              onChange={(e) => setFinalScore(e.target.value)}
              placeholder="Enter final score..."
            />
            <Input
              label="Resolution Note (optional)"
              value={resolutionNote}
              onChange={(e) => setResolutionNote(e.target.value)}
              placeholder="Explain the resolution..."
            />
          </div>
          <div className="flex justify-end gap-2 pt-4 border-t border-border dark:border-border px-4 pb-4">
            <Button variant="ghost" onClick={() => setResolveTarget(null)}>Cancel</Button>
            <Button onClick={handleResolve} disabled={isMutating || !finalScore}>
              {isMutating ? 'Resolving...' : 'Resolve'}
            </Button>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
