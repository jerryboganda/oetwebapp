'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Clock3, Inbox, RefreshCw } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { fetchExpertAssignedReviews, type ExpertAssignedItem } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

function formatRelativeSla(slaDueAt: string): string {
  const due = new Date(slaDueAt).getTime();
  const now = Date.now();
  const diffMs = due - now;
  const absMin = Math.abs(Math.round(diffMs / 60000));
  if (diffMs <= 0) {
    if (absMin < 60) return `${absMin}m overdue`;
    return `${Math.round(absMin / 60)}h overdue`;
  }
  if (absMin < 60) return `${absMin}m left`;
  if (absMin < 60 * 24) return `${Math.round(absMin / 60)}h left`;
  return `${Math.round(absMin / (60 * 24))}d left`;
}

function slaToneVariant(state: ExpertAssignedItem['slaState']): 'success' | 'warning' | 'danger' {
  if (state === 'overdue') return 'danger';
  if (state === 'at_risk') return 'warning';
  return 'success';
}

function letterTypeLabel(value: string | null): string {
  if (!value) return '—';
  return value.replace(/_/g, ' ').replace(/\b\w/g, (m) => m.toUpperCase());
}

function professionLabel(value: string | null): string {
  if (!value) return '—';
  return value.replace(/-/g, ' ').replace(/\b\w/g, (m) => m.toUpperCase());
}

export default function ExpertAssignedQueuePage() {
  const [items, setItems] = useState<ExpertAssignedItem[]>([]);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setRefreshing(true);
    try {
      const data = await fetchExpertAssignedReviews();
      setItems(data);
      setStatus(data.length === 0 ? 'empty' : 'success');
      setErrorMsg(null);
      analytics.track('expert_assigned_queue_viewed', { count: data.length });
      const slaWarnings = data.filter((i) => i.slaState !== 'on_track').length;
      if (slaWarnings > 0) {
        analytics.track('expert_review_sla_warning_shown', { count: slaWarnings });
      }
    } catch (e) {
      const message = (e as Error).message || 'Failed to load assigned reviews.';
      setErrorMsg(message);
      setStatus('error');
    } finally {
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const stats = useMemo(() => {
    let overdue = 0;
    let atRisk = 0;
    for (const item of items) {
      if (item.slaState === 'overdue') overdue++;
      else if (item.slaState === 'at_risk') atRisk++;
    }
    return { total: items.length, overdue, atRisk };
  }, [items]);

  const columns: Column<ExpertAssignedItem>[] = useMemo(() => [
    {
      key: 'task',
      header: 'Task',
      render: (item) => (
        <div>
          <div className="font-semibold text-navy">{item.taskTitle}</div>
          <div className="text-xs text-muted">{letterTypeLabel(item.letterType)}</div>
        </div>
      ),
    },
    {
      key: 'profession',
      header: 'Profession',
      render: (item) => <Badge variant="info">{professionLabel(item.professionId)}</Badge>,
    },
    {
      key: 'learner',
      header: 'Learner',
      render: (item) => <span className="text-sm text-navy">{item.learnerDisplayName || '—'}</span>,
    },
    {
      key: 'sla',
      header: 'SLA',
      render: (item) => (
        <div className="flex flex-col gap-1">
          <Badge variant={slaToneVariant(item.slaState)}>{formatRelativeSla(item.slaDueAt)}</Badge>
          <span className="text-[11px] text-muted">
            {item.turnaroundOption === 'express' ? 'Express' : 'Standard'}
          </span>
        </div>
      ),
    },
    {
      key: 'compensation',
      header: 'Comp',
      render: (item) => (
        <span className="text-sm tabular-nums text-navy">
          {item.reviewerCompensation > 0 ? `£${(item.reviewerCompensation / 100).toFixed(2)}` : '—'}
        </span>
      ),
    },
    {
      key: 'action',
      header: '',
      render: (item) => (
        <Link
          href={`/expert/review/writing/${item.reviewRequestId}`}
          onClick={() => analytics.track('expert_assigned_review_opened', {
            reviewRequestId: item.reviewRequestId,
            slaState: item.slaState,
          })}
          className="inline-flex min-h-9 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary/90"
        >
          Open review
        </Link>
      ),
    },
  ], []);

  return (
    <ExpertRouteWorkspace role="main" aria-label="Assigned to me">
      <ExpertRouteHero
        eyebrow="Expert queue"
        icon={Inbox}
        accent="emerald"
        title="Assigned to me"
        description="Writing reviews the system has auto-assigned to you. Open each one before its SLA deadline. New assignments arrive within ~30 seconds of a learner request; SLA breaches re-pool automatically."
        highlights={[
          { icon: Inbox, label: 'Assigned', value: `${stats.total}` },
          { icon: Clock3, label: 'At risk', value: `${stats.atRisk}` },
          { icon: Clock3, label: 'Overdue', value: `${stats.overdue}` },
        ]}
        aside={
          <div className="flex flex-col gap-3">
            <Button variant="ghost" size="md" disabled={refreshing} onClick={() => void load()}>
              <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} /> Refresh
            </Button>
            <Link
              href="/expert/queue"
              className="inline-flex items-center justify-center gap-2 rounded-xl border border-border bg-surface px-4 py-2 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-background-light"
            >
              Browse open queue
            </Link>
          </div>
        }
      />

      {status === 'error' ? (
        <Card className="border-danger/40 bg-danger/5 p-6 text-sm text-danger">
          <p className="font-semibold">Failed to load assigned reviews.</p>
          <p className="mt-1 text-xs">{errorMsg}</p>
        </Card>
      ) : status === 'empty' ? (
        <Card className="border-border bg-surface">
          <EmptyState
            title="No assignments yet"
            description="The auto-assigner will route writing reviews to you as learners submit them. You can also browse the open queue and claim a review manually."
          />
        </Card>
      ) : (
        <AsyncStateWrapper status={status}>
          <Card className="border-border bg-surface p-6">
            <ExpertRouteSectionHeader
              eyebrow="Active assignments"
              title={`Assigned reviews (${items.length})`}
              description="Sorted by SLA deadline — most urgent first."
              className="mb-4"
            />
            <DataTable data={items} columns={columns} keyExtractor={(item) => item.reviewRequestId} />
          </Card>
        </AsyncStateWrapper>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </ExpertRouteWorkspace>
  );
}
