'use client';

/**
 * System-wide listening AI extraction queue.
 *
 * Backend exposes per-paper draft list (`GET /v1/admin/papers/{paperId}/listening/extractions`)
 * but no aggregate queue endpoint. We aggregate client-side by:
 *   1. listing the most recent Listening papers (status filter optional),
 *   2. fetching the Pending drafts for each, and
 *   3. flattening into a single queue.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Headphones, ArrowLeft, RefreshCw, Loader2 } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { DataTable, type Column } from '@/components/ui/data-table';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { listContentPapers } from '@/lib/content-upload-api';
import {
  adminListeningApproveExtraction,
  adminListeningListExtractions,
  adminListeningRejectExtraction,
} from '@/lib/api';
import type {
  ListeningExtractionDraft,
  ListeningExtractionDraftStatus,
} from '@/lib/types/admin/listening-authoring';

type Row = ListeningExtractionDraft & { paperTitle: string };

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: 'Pending', label: 'Pending' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Rejected', label: 'Rejected' },
  { value: '', label: 'All' },
];

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function ListeningExtractionsQueuePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [statusFilter, setStatusFilter] = useState<string>('Pending');
  const [rows, setRows] = useState<Row[]>([]);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    if (!isAuthenticated || role !== 'admin') return;
    setLoading(true);
    try {
      const papers = await listContentPapers({ subtest: 'listening', pageSize: 100 });
      const titleById = new Map(papers.map((p) => [p.id, p.title]));
      const filter = statusFilter ? (statusFilter as ListeningExtractionDraftStatus) : undefined;
      const all: Row[] = [];
      // Sequential fan-out, capped, to avoid hammering the API.
      for (const p of papers) {
        try {
          const drafts = await adminListeningListExtractions(p.id, filter);
          for (const d of drafts) {
            all.push({ ...d, paperTitle: titleById.get(d.paperId) ?? d.paperId });
          }
        } catch {
          // ignore per-paper failures so one bad paper doesn't sink the queue
        }
      }
      all.sort((a, b) => (a.proposedAt < b.proposedAt ? 1 : -1));
      setRows(all);
    } catch (e) {
      setToast({ variant: 'error', message: `Load queue failed: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated, role, statusFilter]);

  useEffect(() => {
    void load();
  }, [load]);

  const approve = useCallback(
    async (r: Row) => {
      setBusyId(r.id);
      try {
        await adminListeningApproveExtraction(r.paperId, r.id);
        setToast({ variant: 'success', message: `Approved ${r.id.slice(0, 8)}.` });
        await load();
      } catch (e) {
        setToast({ variant: 'error', message: `Approve failed: ${(e as Error).message}` });
      } finally {
        setBusyId(null);
      }
    },
    [load],
  );

  const reject = useCallback(
    async (r: Row) => {
      const reason = window.prompt('Reject reason?');
      if (!reason || !reason.trim()) return;
      setBusyId(r.id);
      try {
        await adminListeningRejectExtraction(r.paperId, r.id, reason.trim());
        setToast({ variant: 'success', message: `Rejected ${r.id.slice(0, 8)}.` });
        await load();
      } catch (e) {
        setToast({ variant: 'error', message: `Reject failed: ${(e as Error).message}` });
      } finally {
        setBusyId(null);
      }
    },
    [load],
  );

  const columns: Column<Row>[] = useMemo(
    () => [
      {
        key: 'paper',
        header: 'Paper',
        render: (r) => (
          <Link
            href={`/admin/content/listening/${r.paperId}`}
            className="font-medium hover:text-primary"
          >
            {r.paperTitle}
          </Link>
        ),
      },
      {
        key: 'status',
        header: 'Status',
        render: (r) => (
          <Badge
            variant={
              r.status === 'Approved' ? 'success' : r.status === 'Rejected' ? 'danger' : 'warning'
            }
          >
            {r.status}
          </Badge>
        ),
      },
      {
        key: 'summary',
        header: 'Summary',
        render: (r) => <span className="text-xs text-muted">{r.summary || '—'}</span>,
      },
      {
        key: 'stub',
        header: 'Stub?',
        render: (r) => (r.isStub ? <Badge variant="muted">stub</Badge> : <span className="text-xs">—</span>),
      },
      {
        key: 'proposed',
        header: 'Proposed',
        render: (r) => <span className="text-xs">{new Date(r.proposedAt).toLocaleString()}</span>,
      },
      {
        key: 'actions',
        header: 'Actions',
        render: (r) =>
          r.status === 'Pending' ? (
            <div className="flex gap-2">
              <Button
                variant="primary"
                size="sm"
                disabled={busyId === r.id}
                onClick={() => void approve(r)}
              >
                Approve
              </Button>
              <Button variant="ghost" size="sm" disabled={busyId === r.id} onClick={() => void reject(r)}>
                Reject
              </Button>
            </div>
          ) : (
            <span className="text-xs text-muted">—</span>
          ),
      },
    ],
    [approve, busyId, reject],
  );

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Listening Extraction Queue">
      <div className="mb-2">
        <Link
          href="/admin/content/listening"
          className="inline-flex items-center gap-1 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-3 w-3" /> Back to Listening papers
        </Link>
      </div>

      <AdminRouteSectionHeader
        icon={<Headphones className="w-6 h-6" />}
        title="Listening extraction queue"
        description="All AI-proposed structure drafts across every Listening paper. Approve to overwrite the paper's authored 42-item structure; reject with a reason for audit."
      />

      <AdminRoutePanel title="Filters">
        <div className="flex flex-wrap items-end gap-2">
          <Select
            label="Status"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={STATUS_OPTIONS}
          />
          <Button variant="ghost" onClick={() => void load()} disabled={loading}>
            {loading ? <Loader2 className="h-3 w-3 animate-spin" /> : <RefreshCw className="h-3 w-3" />}
            Refresh
          </Button>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title={`Drafts (${rows.length})`}>
        <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} />
      </AdminRoutePanel>

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
