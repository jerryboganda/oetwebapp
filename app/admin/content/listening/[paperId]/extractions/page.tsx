'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  BrainCircuit,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Loader2,
  XCircle,
} from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Modal } from '@/components/ui/modal';
import { Textarea } from '@/components/admin/ui/textarea';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  approveListeningExtractionDraft,
  listListeningExtractionDrafts,
  proposeListeningStructure,
  rejectListeningExtractionDraft,
  type ListeningExtractionDraftDto,
  type ListeningExtractionStatus,
} from '@/lib/listening-authoring-api';

type LoadState = 'loading' | 'ready' | 'error';

function firstParam(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString(undefined, {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
  } catch {
    return iso;
  }
}

const STATUS_TABS = [
  { id: 'Pending', label: 'Pending' },
  { id: 'Approved', label: 'Approved' },
  { id: 'Rejected', label: 'Rejected' },
];

function statusBadgeVariant(status: ListeningExtractionStatus) {
  if (status === 'Approved') return 'success' as const;
  if (status === 'Rejected') return 'danger' as const;
  return 'warning' as const;
}

// ─── Decision dialogs ───────────────────────────────────────────────────────

interface DecisionDialogProps {
  open: boolean;
  mode: 'approve' | 'reject';
  onClose: () => void;
  onConfirm: (reason: string) => Promise<void>;
}

function DecisionDialog({ open, mode, onClose, onConfirm }: DecisionDialogProps) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleConfirm = async () => {
    if (mode === 'reject' && !reason.trim()) {
      setError('A reason is required when rejecting a draft.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onConfirm(reason.trim());
      setReason('');
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
  };

  const handleClose = () => {
    if (!busy) {
      setReason('');
      setError(null);
      onClose();
    }
  };

  return (
    <Modal
      open={open}
      onClose={handleClose}
      title={mode === 'approve' ? 'Approve Draft' : 'Reject Draft'}
      size="md"
    >
      <div className="space-y-4">
        <p className="text-sm text-admin-fg-muted">
          {mode === 'approve'
            ? 'Optionally add a note before approving this draft. Approving will set it as the active structure.'
            : 'Provide a reason for rejection. This will be recorded with the decision.'}
        </p>
        <Textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={3}
          placeholder={
            mode === 'reject'
              ? 'e.g. Questions do not match the audio transcript sections.'
              : 'e.g. Questions look accurate and well-formed.'
          }
        />
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="flex justify-end gap-2 pt-1">
          <Button variant="ghost" onClick={handleClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            variant={mode === 'approve' ? 'primary' : 'destructive'}
            onClick={handleConfirm}
            disabled={busy}
          >
            {busy ? <Loader2 className="h-4 w-4 animate-spin mr-1.5" /> : null}
            {mode === 'approve' ? 'Approve' : 'Reject'}
          </Button>
        </div>
      </div>
    </Modal>
  );
}

// ─── Single draft card ─────────────────────────────────────────────────────

interface DraftCardProps {
  draft: ListeningExtractionDraftDto;
  paperId: string;
  onApprove: (draftId: string, reason: string) => Promise<void>;
  onReject: (draftId: string, reason: string) => Promise<void>;
}

function DraftCard({ draft, paperId: _paperId, onApprove, onReject }: DraftCardProps) {
  const [expanded, setExpanded] = useState(false);
  const [dialog, setDialog] = useState<'approve' | 'reject' | null>(null);

  const isPending = draft.status === 'Pending';

  return (
    <Card>
      <CardContent className="space-y-4 p-5">
        {/* Header row */}
        <div className="flex flex-wrap items-start gap-3">
          <div className="flex-1 space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={statusBadgeVariant(draft.status)}>{draft.status}</Badge>
              {draft.isStub && (
                <Badge variant="warning">
                  Stub: AI could not generate full structure
                </Badge>
              )}
            </div>
            <p className="text-xs text-admin-fg-muted">
              Proposed: {formatDate(draft.proposedAt)}
            </p>
            {draft.isStub && draft.stubReason && (
              <p className="text-xs text-amber-700">{draft.stubReason}</p>
            )}
          </div>
          {/* Question count pill */}
          <span className="rounded-full bg-admin-bg-subtle px-3 py-1 text-xs font-semibold text-admin-fg-muted">
            {draft.questions.length} / 42 questions
          </span>
        </div>

        {/* Summary */}
        {draft.summary && (
          <p className="text-sm text-admin-fg-strong">{draft.summary}</p>
        )}

        {/* Decision info for non-pending */}
        {!isPending && draft.decidedAt && (
          <div className="rounded-admin bg-admin-bg-subtle p-3 text-xs text-admin-fg-muted space-y-0.5">
            <p>
              <span className="font-semibold">Decided:</span> {formatDate(draft.decidedAt)}
            </p>
            {draft.decisionReason && (
              <p>
                <span className="font-semibold">Reason:</span> {draft.decisionReason}
              </p>
            )}
          </div>
        )}

        {/* View Questions toggle */}
        {draft.questions.length > 0 && (
          <div>
            <button
              onClick={() => setExpanded((v) => !v)}
              className="inline-flex items-center gap-1.5 text-sm font-semibold text-[var(--admin-primary)] hover:underline"
            >
              {expanded ? (
                <>
                  <ChevronUp className="h-4 w-4" /> Hide Questions
                </>
              ) : (
                <>
                  <ChevronDown className="h-4 w-4" /> View Questions ({draft.questions.length})
                </>
              )}
            </button>
            {expanded && (
              <div className="mt-3 overflow-x-auto rounded-admin border border-admin-border">
                <table className="w-full min-w-[600px] text-left text-sm">
                  <thead>
                    <tr className="border-b border-admin-border bg-admin-bg-subtle text-xs font-semibold uppercase tracking-widest text-admin-fg-muted">
                      <th scope="col" className="py-2 pr-3 pl-3">#</th>
                      <th scope="col" className="py-2 pr-3">Part</th>
                      <th scope="col" className="py-2 pr-3">Type</th>
                      <th scope="col" className="py-2 pr-3">Stem</th>
                      <th scope="col" className="py-2 pr-3">Options / Answer</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-admin-border px-3">
                    {draft.questions.map((q) => (
                      <tr key={q.id ?? q.number} className="border-b border-admin-border last:border-0">
                        <td className="py-2 pl-3 pr-3 font-mono text-xs text-admin-fg-muted">{q.number}</td>
                        <td className="py-2 pr-3">
                          <Badge variant="info" size="sm">{q.partCode}</Badge>
                        </td>
                        <td className="py-2 pr-3">
                          <span className="text-xs uppercase tracking-widest text-admin-fg-muted">
                            {q.type === 'multiple_choice_3' ? 'MCQ' : 'Gap fill'}
                          </span>
                        </td>
                        <td className="max-w-xs py-2 pr-3 text-sm text-admin-fg-strong">
                          <span className="line-clamp-2">{q.stem || <em className="text-admin-fg-muted">empty</em>}</span>
                        </td>
                        <td className="py-2 pr-3 text-sm text-admin-fg-strong">
                          {q.type === 'multiple_choice_3'
                            ? q.options.map((o, i) => (
                                <span
                                  key={i}
                                  className={`mr-1 inline-block rounded px-1.5 py-0.5 text-xs ${
                                    o === q.correctAnswer
                                      ? 'bg-emerald-100 text-emerald-800 font-semibold'
                                      : 'bg-admin-bg-subtle text-admin-fg-muted'
                                  }`}
                                >
                                  {String.fromCharCode(65 + i)}. {o || '—'}
                                </span>
                              ))
                            : q.correctAnswer || <em className="text-admin-fg-muted">empty</em>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Pending actions */}
        {isPending && (
          <div className="flex gap-2 pt-1">
            <Button variant="primary" size="sm" onClick={() => setDialog('approve')}>
              <CheckCircle2 className="h-4 w-4 mr-1.5" />
              Approve
            </Button>
            <Button variant="destructive" size="sm" onClick={() => setDialog('reject')}>
              <XCircle className="h-4 w-4 mr-1.5" />
              Reject
            </Button>
          </div>
        )}

        <DecisionDialog
          open={dialog === 'approve'}
          mode="approve"
          onClose={() => setDialog(null)}
          onConfirm={(reason) => onApprove(draft.id, reason)}
        />
        <DecisionDialog
          open={dialog === 'reject'}
          mode="reject"
          onClose={() => setDialog(null)}
          onConfirm={(reason) => onReject(draft.id, reason)}
        />
      </CardContent>
    </Card>
  );
}

// ─── Page ──────────────────────────────────────────────────────────────────

export default function AdminListeningExtractionsPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();

  const [activeTab, setActiveTab] = useState<ListeningExtractionStatus>('Pending');
  const [state, setState] = useState<LoadState>('loading');
  const [drafts, setDrafts] = useState<ListeningExtractionDraftDto[]>([]);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [triggering, setTriggering] = useState(false);
  const [triggerMsg, setTriggerMsg] = useState<string | null>(null);
  const [triggerError, setTriggerError] = useState<string | null>(null);

  const load = useCallback(
    async (status: ListeningExtractionStatus) => {
      if (!paperId) return;
      setState('loading');
      setFetchError(null);
      try {
        const result = await listListeningExtractionDrafts(paperId, status);
        setDrafts(result);
        setState('ready');
      } catch (e) {
        setFetchError(e instanceof Error ? e.message : 'Could not load drafts.');
        setState('error');
      }
    },
    [paperId],
  );

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load(activeTab);
  }, [isAuthenticated, role, load, activeTab]);

  const handleTabChange = (id: string) => {
    setActiveTab(id as ListeningExtractionStatus);
  };

  const handleTrigger = async () => {
    if (!paperId) return;
    setTriggering(true);
    setTriggerMsg(null);
    setTriggerError(null);
    try {
      await proposeListeningStructure(paperId);
      setTriggerMsg('Extraction triggered. A new draft will appear in the Pending tab shortly.');
      if (activeTab === 'Pending') {
        await load('Pending');
      } else {
        setActiveTab('Pending');
      }
    } catch (e) {
      setTriggerError(e instanceof Error ? e.message : 'Failed to trigger extraction.');
    } finally {
      setTriggering(false);
    }
  };

  const handleApprove = async (draftId: string, reason: string) => {
    if (!paperId) return;
    await approveListeningExtractionDraft(paperId, draftId, reason);
    await load(activeTab);
  };

  const handleReject = async (draftId: string, reason: string) => {
    if (!paperId) return;
    await rejectListeningExtractionDraft(paperId, draftId, reason);
    await load(activeTab);
  };

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Extractions' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="AI Extraction Drafts" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="AI Authoring"
      icon={<BrainCircuit className="h-5 w-5" />}
      title="AI Extraction Drafts"
      description={`Paper ${paperId ?? ''}. Review, approve, or reject AI-proposed question structures.`}
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to structure
            </Link>
          </Button>
          <Button variant="primary" size="sm" onClick={handleTrigger} disabled={triggering}>
            {triggering ? <Loader2 className="h-4 w-4 animate-spin mr-1.5" /> : <BrainCircuit className="h-4 w-4 mr-1.5" />}
            Trigger New Extraction
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        {triggerMsg && <InlineAlert variant="success">{triggerMsg}</InlineAlert>}
        {triggerError && <InlineAlert variant="error">{triggerError}</InlineAlert>}

        <Tabs tabs={STATUS_TABS} activeTab={activeTab} onChange={handleTabChange} />

        {STATUS_TABS.map((tab) => (
          <TabPanel key={tab.id} id={tab.id} activeTab={activeTab}>
            <Card>
              <CardHeader><CardTitle>{tab.label} Drafts</CardTitle></CardHeader>
              <CardContent className="space-y-4">
                {state === 'loading' && <Skeleton className="h-48 rounded-admin" />}
                {state === 'error' && fetchError && (
                  <InlineAlert variant="error">{fetchError}</InlineAlert>
                )}
                {state === 'ready' && drafts.length === 0 && (
                  <p className="py-8 text-center text-sm text-admin-fg-muted">
                    No {tab.label.toLowerCase()} drafts for this paper.
                  </p>
                )}
                {state === 'ready' && drafts.length > 0 && (
                  <div className="space-y-4">
                    {drafts.map((draft) => (
                      <DraftCard
                        key={draft.id}
                        draft={draft}
                        paperId={paperId ?? ''}
                        onApprove={handleApprove}
                        onReject={handleReject}
                      />
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </TabPanel>
        ))}
      </div>
    </AdminSettingsLayout>
  );
}
