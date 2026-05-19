'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, RefreshCw, ThumbsUp, ThumbsDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Modal } from '@/components/ui/modal';
import {
  adminListeningApproveExtraction,
  adminListeningListExtractions,
  adminListeningRejectExtraction,
} from '@/lib/api';
import type { ListeningExtractionDraft } from '@/lib/types/admin/listening-authoring';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

export function ExtractionsTab({ paperId, onToast }: Props) {
  const [items, setItems] = useState<ListeningExtractionDraft[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rejectFor, setRejectFor] = useState<ListeningExtractionDraft | null>(null);
  const [rejectReason, setRejectReason] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListeningListExtractions(paperId);
      setItems(data);
    } catch (e) {
      onToast('error', `Load extractions failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const approve = useCallback(
    async (d: ListeningExtractionDraft) => {
      setBusyId(d.id);
      try {
        await adminListeningApproveExtraction(paperId, d.id);
        onToast('success', `Draft ${d.id.slice(0, 8)} approved.`);
        await load();
      } catch (e) {
        onToast('error', `Approve failed: ${(e as Error).message}`);
      } finally {
        setBusyId(null);
      }
    },
    [load, onToast, paperId],
  );

  const reject = useCallback(async () => {
    if (!rejectFor) return;
    if (!rejectReason.trim()) {
      onToast('error', 'Reject reason is required.');
      return;
    }
    setBusyId(rejectFor.id);
    try {
      await adminListeningRejectExtraction(paperId, rejectFor.id, rejectReason.trim());
      onToast('success', `Draft ${rejectFor.id.slice(0, 8)} rejected.`);
      setRejectFor(null);
      setRejectReason('');
      await load();
    } catch (e) {
      onToast('error', `Reject failed: ${(e as Error).message}`);
    } finally {
      setBusyId(null);
    }
  }, [load, onToast, paperId, rejectFor, rejectReason]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading drafts…
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted">{items.length} extraction draft(s)</p>
        <Button variant="ghost" size="sm" onClick={() => void load()}>
          <RefreshCw className="h-3 w-3" /> Refresh
        </Button>
      </div>

      {items.length === 0 ? (
        <p className="text-sm text-muted">No drafts yet. Use Extracts → AI Propose to create one.</p>
      ) : (
        items.map((d) => (
          <AdminRoutePanel key={d.id}>
            <div className="mb-2 flex items-center gap-2">
              <Badge
                variant={
                  d.status === 'Approved' ? 'success' : d.status === 'Rejected' ? 'danger' : 'warning'
                }
              >
                {d.status}
              </Badge>
              <span className="font-mono text-xs">{d.id.slice(0, 12)}</span>
              {d.isStub && <Badge variant="muted">stub</Badge>}
            </div>
            <p className="text-sm text-navy">{d.summary || '(no summary)'}</p>
            {d.stubReason && <p className="mt-1 text-xs text-warning">stub reason: {d.stubReason}</p>}
            <div className="mt-2 text-xs text-muted">
              Proposed {new Date(d.proposedAt).toLocaleString()} by {d.proposedByUserId ?? 'system'}
            </div>
            {d.decidedAt && (
              <div className="text-xs text-muted">
                Decided {new Date(d.decidedAt).toLocaleString()} by {d.decidedByUserId ?? '—'}:{' '}
                {d.decisionReason ?? '—'}
              </div>
            )}
            {d.status === 'Pending' && (
              <div className="mt-3 flex gap-2">
                <Button
                  variant="primary"
                  size="sm"
                  disabled={busyId === d.id}
                  onClick={() => void approve(d)}
                >
                  {busyId === d.id ? (
                    <Loader2 className="h-3 w-3 animate-spin" />
                  ) : (
                    <ThumbsUp className="h-3 w-3" />
                  )}
                  Approve
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={busyId === d.id}
                  onClick={() => {
                    setRejectFor(d);
                    setRejectReason('');
                  }}
                >
                  <ThumbsDown className="h-3 w-3" /> Reject
                </Button>
              </div>
            )}
          </AdminRoutePanel>
        ))
      )}

      <Modal
        open={!!rejectFor}
        onClose={() => setRejectFor(null)}
        title={`Reject draft ${rejectFor?.id.slice(0, 12) ?? ''}`}
      >
        <div className="space-y-3">
          <Textarea
            label="Reason"
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            placeholder="Why is this draft being rejected?"
          />
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setRejectFor(null)}>
              Cancel
            </Button>
            <Button variant="primary" onClick={() => void reject()}>
              Confirm reject
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
