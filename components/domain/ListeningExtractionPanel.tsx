'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { Sparkles, RefreshCw, CheckCircle2, XCircle } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs } from '@/components/ui/tabs';
import { Textarea } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import { getSurfaceMotion, getMotionPresenceMode, prefersReducedMotion } from '@/lib/motion';
import {
  approveListeningExtractionDraft,
  getListeningExtractionDraft,
  getListeningStructure,
  listListeningExtractionDrafts,
  proposeListeningStructure,
  rejectListeningExtractionDraft,
  type ListeningAuthoredQuestion,
  type ListeningExtractionDraftDto,
  type ListeningExtractionStatus,
} from '@/lib/listening-authoring-api';

/**
 * Admin diff/approve UI for AI-proposed Listening structures (Gap B7).
 *
 * Sits between {@link ListeningExtractMetadataEditor} and
 * {@link ListeningStructureEditor} on the paper editor page. The previous
 * "Propose with AI" button silently overwrote the in-memory question list,
 * destroying unsaved edits and bypassing reviewer audit. This panel:
 *   1. Calls POST /v1/admin/papers/{paperId}/listening/extract to persist a
 *      Pending draft (no side-effect on the authored structure).
 *   2. Lists Pending drafts (and optionally decided ones) as tabs.
 *   3. Shows a side-by-side diff of the active draft against the current
 *      authored structure with green/blue/red highlights.
 *   4. Approves (replaces the structure + audit log) or rejects (audit only,
 *      requires reason ≤ 500 chars) the active draft.
 */

const REJECT_REASON_MAX = 500;

export interface ListeningExtractionPanelProps {
  paperId: string;
  /**
   * Invoked after a successful Approve so the parent page can re-fetch the
   * structure (e.g. via `getListeningStructure`) to surface the applied
   * questions in the editor below.
   */
  onApplied?: () => void;
}

type ToastState = { variant: 'success' | 'error' | 'info'; message: string } | null;

interface ApiError extends Error {
  status?: number;
  detail?: { error?: string } | unknown;
}

function readApiError(e: unknown, fallback: string): { status: number | undefined; message: string } {
  const err = e as ApiError;
  const status = typeof err?.status === 'number' ? err.status : undefined;
  const detail = err?.detail as { error?: string } | undefined;
  const message = detail?.error ?? err?.message ?? fallback;
  return { status, message };
}

function statusBadge(status: ListeningExtractionStatus, isStub: boolean) {
  if (status === 'Pending') {
    return (
      <Badge variant={isStub ? 'muted' : 'warning'}>
        {isStub ? 'Stub · Pending' : 'Pending'}
      </Badge>
    );
  }
  if (status === 'Approved') return <Badge variant="success">Approved</Badge>;
  return <Badge variant="danger">Rejected</Badge>;
}

function formatProposedAt(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString();
  } catch {
    return iso;
  }
}

function truncate(text: string, max: number): { display: string; truncated: boolean } {
  if (!text) return { display: '', truncated: false };
  if (text.length <= max) return { display: text, truncated: false };
  return { display: text.slice(0, max).trimEnd() + '…', truncated: true };
}

export function ListeningExtractionPanel({ paperId, onApplied }: ListeningExtractionPanelProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const itemMotion = getSurfaceMotion('item', reducedMotion);

  const [drafts, setDrafts] = useState<ListeningExtractionDraftDto[]>([]);
  const [activeDraftId, setActiveDraftId] = useState<string | null>(null);
  const [showDecided, setShowDecided] = useState(false);
  const [loadingList, setLoadingList] = useState(true);
  const [proposing, setProposing] = useState(false);
  const [acting, setActing] = useState(false);
  const [currentQuestions, setCurrentQuestions] = useState<ListeningAuthoredQuestion[]>([]);
  const [currentLoadError, setCurrentLoadError] = useState<string | null>(null);
  const [confirmApprove, setConfirmApprove] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [toast, setToast] = useState<ToastState>(null);
  const [quotaExceeded, setQuotaExceeded] = useState(false);

  const refreshList = useCallback(async () => {
    setLoadingList(true);
    try {
      const [pending, decided] = await Promise.all([
        listListeningExtractionDrafts(paperId, 'Pending'),
        showDecided
          ? Promise.all([
              listListeningExtractionDrafts(paperId, 'Approved'),
              listListeningExtractionDrafts(paperId, 'Rejected'),
            ]).then(([a, r]) => [...a, ...r])
          : Promise.resolve<ListeningExtractionDraftDto[]>([]),
      ]);
      const combined = [...pending, ...decided].sort(
        (a, b) => new Date(b.proposedAt).getTime() - new Date(a.proposedAt).getTime(),
      );
      setDrafts(combined);
      setActiveDraftId((prev) => {
        if (prev && combined.some((d) => d.id === prev)) return prev;
        const firstPending = combined.find((d) => d.status === 'Pending');
        return firstPending?.id ?? combined[0]?.id ?? null;
      });
    } catch (e) {
      const { message } = readApiError(e, 'Failed to load extraction drafts.');
      setToast({ variant: 'error', message });
    } finally {
      setLoadingList(false);
    }
  }, [paperId, showDecided]);

  const refreshCurrent = useCallback(async () => {
    try {
      const doc = await getListeningStructure(paperId);
      setCurrentQuestions(doc.questions);
      setCurrentLoadError(null);
    } catch (e) {
      setCurrentQuestions([]);
      const { message } = readApiError(e, 'Failed to load the current authored structure.');
      setCurrentLoadError(message);
    }
  }, [paperId]);

  useEffect(() => { void refreshList(); }, [refreshList]);
  useEffect(() => { void refreshCurrent(); }, [refreshCurrent]);

  const activeDraft = useMemo(
    () => drafts.find((d) => d.id === activeDraftId) ?? null,
    [drafts, activeDraftId],
  );

  const propose = useCallback(async () => {
    setProposing(true);
    try {
      const proposal = await proposeListeningStructure(paperId);
      // The propose endpoint returns the proposal snapshot; refresh the list
      // to pick up the persisted draft (and any other Pending drafts that
      // landed concurrently).
      await refreshList();
      setActiveDraftId(proposal.draftId);
      setQuotaExceeded(false);
      setToast({
        variant: 'success',
        message: proposal.isStub
          ? `AI stub proposal saved as draft (re-propose once extraction is fixed). ${proposal.stubReason ?? ''}`.trim()
          : 'AI proposal saved as draft. Review the diff before applying.',
      });
    } catch (e) {
      const { status, message } = readApiError(e, 'Extraction failed.');
      if (status === 402 || (status === 409 && /quota|maximum/i.test(message))) {
        setQuotaExceeded(true);
        setToast({ variant: 'error', message });
      } else {
        setToast({ variant: 'error', message });
      }
    } finally {
      setProposing(false);
    }
  }, [paperId, refreshList]);

  const approve = useCallback(async () => {
    if (!activeDraft) return;
    setActing(true);
    try {
      await approveListeningExtractionDraft(paperId, activeDraft.id, '');
      setConfirmApprove(false);
      setToast({ variant: 'success', message: 'Draft approved — structure replaced and audit logged.' });
      await Promise.all([refreshList(), refreshCurrent()]);
      onApplied?.();
    } catch (e) {
      const { status, message } = readApiError(e, 'Approve failed.');
      setToast({ variant: 'error', message: status === 409 ? `${message} Refreshing draft list.` : message });
      if (status === 409) await refreshList();
      setConfirmApprove(false);
    } finally {
      setActing(false);
    }
  }, [activeDraft, paperId, refreshList, refreshCurrent, onApplied]);

  const reject = useCallback(async (reasonOverride?: string) => {
    if (!activeDraft) return;
    const trimmed = (reasonOverride ?? rejectReason).trim();
    if (!trimmed) return;
    setActing(true);
    try {
      await rejectListeningExtractionDraft(paperId, activeDraft.id, trimmed);
      setRejectOpen(false);
      setRejectReason('');
      setToast({ variant: 'success', message: 'Draft rejected — audit logged.' });
      await refreshList();
    } catch (e) {
      const { status, message } = readApiError(e, 'Reject failed.');
      setToast({ variant: 'error', message: status === 409 ? `${message} Refreshing draft list.` : message });
      if (status === 409) await refreshList();
      setRejectOpen(false);
    } finally {
      setActing(false);
    }
  }, [activeDraft, paperId, rejectReason, refreshList]);

  // Build tab strip — Pending first, then optional decided.
  const tabs = useMemo(
    () =>
      drafts.map((d, index) => ({
        id: d.id,
        label: d.status === 'Pending'
          ? `Pending #${drafts.length - index}`
          : `${d.status} · ${formatProposedAt(d.proposedAt)}`,
      })),
    [drafts],
  );

  // Build the diff rows: union of question numbers across left + right.
  const diffRows = useMemo(() => {
    const right = activeDraft?.questions ?? [];
    const numbers = new Set<number>();
    for (const q of currentQuestions) numbers.add(q.number);
    for (const q of right) numbers.add(q.number);
    const ordered = [...numbers].sort((a, b) => a - b);
    const leftMap = new Map(currentQuestions.map((q) => [q.number, q]));
    const rightMap = new Map(right.map((q) => [q.number, q]));
    return ordered.map((n) => ({
      number: n,
      left: leftMap.get(n) ?? null,
      right: rightMap.get(n) ?? null,
    }));
  }, [activeDraft, currentQuestions]);

  const canReject = activeDraft?.status === 'Pending';
  const canDecide = canReject && activeDraft?.isStub !== true && !currentLoadError;

  return (
    <>
      <AdminRoutePanel
        title="Listening AI extraction"
        description="Review AI-proposed Listening structures before they replace the authored question map. Each proposal is persisted as a Pending draft and audit-logged on Approve / Reject."
        actions={
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={() => void refreshList()} disabled={loadingList || acting}>
              <RefreshCw className="w-4 h-4 mr-1" /> Reload
            </Button>
            <label className="inline-flex items-center gap-2 text-xs text-muted">
              <input
                type="checkbox"
                checked={showDecided}
                onChange={(e) => setShowDecided(e.target.checked)}
                className="rounded border-gray-300"
              />
              Show decided
            </label>
            <Button
              variant="primary"
              size="sm"
              onClick={() => void propose()}
              loading={proposing}
              disabled={proposing}
            >
              <Sparkles className="w-4 h-4 mr-1" /> Propose with AI
            </Button>
          </div>
        }
      >
        <div className="space-y-5">
          {quotaExceeded && (
            <InlineAlert variant="error" title="Admin AI quota exceeded">
              The grounded AI gateway refused this request because the admin extraction quota has been
              spent. Contact billing to lift the cap, or retry once the quota resets.
            </InlineAlert>
          )}

          {loadingList ? (
            <Skeleton className="h-32" />
          ) : drafts.length === 0 ? (
            <EmptyState onPropose={() => void propose()} disabled={proposing} />
          ) : (
            <>
              <Tabs tabs={tabs} activeTab={activeDraftId ?? ''} onChange={setActiveDraftId} />

              {activeDraft && (
                <AnimatePresence mode={getMotionPresenceMode(reducedMotion)} initial={false}>
                  <motion.div key={activeDraft.id} {...itemMotion} className="space-y-4">
                    <DraftHeader draft={activeDraft} />

                    {activeDraft.isStub && (
                      <InlineAlert variant="warning" title="AI returned a stub proposal">
                        {activeDraft.stubReason ?? 'No reason supplied.'}
                        {' '}Stub proposals cannot be approved. Re-propose after fixing the source-PDF extraction.
                      </InlineAlert>
                    )}

                    {currentLoadError && (
                      <InlineAlert variant="error" title="Current structure could not be loaded">
                        {currentLoadError} Approval is disabled until the current authored structure is visible for review.
                      </InlineAlert>
                    )}

                    <DiffTable rows={diffRows} />

                    <div className="flex flex-wrap items-center justify-end gap-2 border-t border-gray-100 pt-4">
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => void propose()}
                        loading={proposing}
                        disabled={proposing || acting}
                      >
                        <Sparkles className="w-4 h-4 mr-1" /> Re-propose
                      </Button>
                      <Button
                        variant="destructive"
                        size="sm"
                        onClick={() => { setRejectOpen(true); }}
                        disabled={!canReject || acting}
                      >
                        <XCircle className="w-4 h-4 mr-1" /> Reject
                      </Button>
                      <Button
                        variant="primary"
                        size="sm"
                        onClick={() => setConfirmApprove(true)}
                        disabled={!canDecide || acting}
                      >
                        <CheckCircle2 className="w-4 h-4 mr-1" /> Approve &amp; apply
                      </Button>
                    </div>
                  </motion.div>
                </AnimatePresence>
              )}
            </>
          )}
        </div>
      </AdminRoutePanel>

      <Modal
        open={confirmApprove}
        onClose={() => (acting ? undefined : setConfirmApprove(false))}
        title="Approve AI-proposed structure?"
        size="md"
      >
        <div className="space-y-4">
          <p className="text-sm text-navy">
            This will replace the current authored structure with{' '}
            <strong>{activeDraft?.questions.length ?? 0}</strong> questions from this draft.
            The change is audit-logged.
          </p>
          {activeDraft?.isStub && (
            <InlineAlert variant="warning">
              This proposal is a stub and cannot be approved. Re-propose after source extraction succeeds.
            </InlineAlert>
          )}
          <div className="flex items-center justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={() => setConfirmApprove(false)} disabled={acting}>
              Cancel
            </Button>
            <Button variant="primary" size="sm" onClick={() => void approve()} loading={acting} disabled={acting || !canDecide}>
              Approve &amp; apply
            </Button>
          </div>
        </div>
      </Modal>

      <Modal
        open={rejectOpen}
        onClose={() => (acting ? undefined : setRejectOpen(false))}
        title="Reject AI-proposed structure"
        size="md"
      >
        <RejectForm
          key={activeDraft?.id ?? 'none'}
          acting={acting}
          maxChars={REJECT_REASON_MAX}
          onCancel={() => setRejectOpen(false)}
          onConfirm={(reason) => { setRejectReason(reason); void reject(reason); }}
        />
      </Modal>

      {toast && (
        <Toast
          variant={toast.variant === 'info' ? 'info' : toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </>
  );
}

function EmptyState({ onPropose, disabled }: { onPropose: () => void; disabled: boolean }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 rounded-2xl border border-dashed border-gray-300 bg-background-light px-6 py-12 text-center">
      <Sparkles className="h-8 w-8 text-primary" aria-hidden="true" />
      <div className="space-y-1">
        <p className="text-base font-semibold text-navy">No AI proposals yet</p>
        <p className="max-w-md text-sm text-muted">
          Run the grounded AI gateway against this paper&apos;s Question Paper + Audio Script to draft a
          42-item Listening structure. The proposal lands here as a Pending draft for diff &amp; review.
        </p>
      </div>
      <Button variant="primary" size="md" onClick={onPropose} disabled={disabled}>
        <Sparkles className="w-4 h-4 mr-1" /> Propose with AI
      </Button>
    </div>
  );
}

function DraftHeader({ draft }: { draft: ListeningExtractionDraftDto }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3 rounded-2xl border border-gray-100 bg-surface px-4 py-3">
      <div className="space-y-1">
        <div className="flex items-center gap-2">
          {statusBadge(draft.status, draft.isStub)}
          <span className="text-xs text-muted">
            Proposed {formatProposedAt(draft.proposedAt)}
            {draft.proposedByUserId ? ` · by ${draft.proposedByUserId}` : ''}
          </span>
        </div>
        {draft.summary && (
          <p className="max-w-3xl text-sm text-navy">{draft.summary}</p>
        )}
        {draft.status !== 'Pending' && draft.decisionReason && (
          <p className="text-xs italic text-muted">
            Decision reason: &ldquo;{draft.decisionReason}&rdquo;
          </p>
        )}
      </div>
      <div className="text-right text-xs text-muted">
        {draft.questions.length} / 42 questions
      </div>
    </div>
  );
}

interface DiffRow {
  number: number;
  left: ListeningAuthoredQuestion | null;
  right: ListeningAuthoredQuestion | null;
}

function questionSignature(q: ListeningAuthoredQuestion | null): string {
  if (!q) return '';
  return JSON.stringify({
    partCode: q.partCode,
    type: q.type,
    stem: q.stem,
    options: q.options,
    correctAnswer: q.correctAnswer,
    acceptedAnswers: q.acceptedAnswers,
    explanation: q.explanation,
    skillTag: q.skillTag,
    transcriptExcerpt: q.transcriptExcerpt,
    distractorExplanation: q.distractorExplanation,
    points: q.points,
    optionDistractorWhy: q.optionDistractorWhy,
    optionDistractorCategory: q.optionDistractorCategory,
    speakerAttitude: q.speakerAttitude,
    transcriptEvidenceStartMs: q.transcriptEvidenceStartMs,
    transcriptEvidenceEndMs: q.transcriptEvidenceEndMs,
  });
}

function diffKindForRow(row: DiffRow): 'unchanged' | 'changed' | 'added' | 'removed' | 'empty' {
  const leftEmpty = !row.left || (!row.left.stem && !row.left.correctAnswer);
  const rightEmpty = !row.right || (!row.right.stem && !row.right.correctAnswer);
  if (leftEmpty && rightEmpty) return 'empty';
  if (leftEmpty && !rightEmpty) return 'added';
  if (!leftEmpty && rightEmpty) return 'removed';
  return questionSignature(row.left) === questionSignature(row.right) ? 'unchanged' : 'changed';
}

const DIFF_TONE: Record<ReturnType<typeof diffKindForRow>, string> = {
  unchanged: 'bg-white',
  changed: 'bg-emerald-50',
  added: 'bg-blue-50',
  removed: 'bg-red-50',
  empty: 'bg-gray-50',
};

const DIFF_LABEL: Record<ReturnType<typeof diffKindForRow>, string> = {
  unchanged: 'Unchanged',
  changed: 'Changed',
  added: 'Added',
  removed: 'Removed',
  empty: 'Empty',
};

function DiffTable({ rows }: { rows: DiffRow[] }) {
  return (
    <div className="overflow-x-auto rounded-2xl border border-gray-200">
      <div className="flex flex-wrap gap-2 border-b border-gray-100 bg-background-light px-3 py-2 text-xs text-muted">
        {(['changed', 'added', 'removed', 'unchanged'] as const).map((kind) => (
          <span key={kind} className={cn('rounded px-2 py-1', DIFF_TONE[kind])}>{DIFF_LABEL[kind]}</span>
        ))}
      </div>
      <table className="w-full table-fixed text-sm">
        <thead className="bg-background-light text-left text-xs uppercase tracking-wide text-muted">
          <tr>
            <th className="w-12 px-3 py-2">#</th>
            <th className="px-3 py-2">Current authored</th>
            <th className="px-3 py-2">AI proposal</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td colSpan={3} className="px-3 py-6 text-center text-sm text-muted">
                Draft has no questions to diff.
              </td>
            </tr>
          )}
          {rows.map((row) => {
            const kind = diffKindForRow(row);
            return (
              <tr
                key={row.number}
                data-testid={`diff-row-${row.number}`}
                data-diff-kind={kind}
                className={cn('border-t border-gray-100 align-top', DIFF_TONE[kind])}
              >
                <td className="px-3 py-2 text-xs font-semibold text-navy">
                  <div>{row.number}</div>
                  <div className="mt-1 font-normal text-muted">{DIFF_LABEL[kind]}</div>
                </td>
                <DiffCell q={row.left} />
                <DiffCell q={row.right} />
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function DiffCell({ q }: { q: ListeningAuthoredQuestion | null }) {
  if (!q) {
    return <td className="px-3 py-2 text-xs italic text-muted">— missing —</td>;
  }
  const stem = truncate(q.stem ?? '', 80);
  const answer = q.correctAnswer ?? '';
  return (
    <td className="px-3 py-2 align-top">
      <div className="space-y-1">
        <div className="text-xs text-muted">{q.partCode} · {q.type}</div>
        {stem.truncated ? (
          <details className="text-sm text-navy">
            <summary className="cursor-pointer">{stem.display}</summary>
            <p className="mt-1 whitespace-pre-wrap text-sm text-navy">{q.stem}</p>
          </details>
        ) : (
          <p className="text-sm text-navy">{stem.display || <span className="italic text-muted">(empty stem)</span>}</p>
        )}
        <p className="text-xs">
          <span className="font-semibold text-navy">Answer:</span>{' '}
          <span className="text-navy">{answer || <span className="italic text-muted">(none)</span>}</span>
        </p>
      </div>
    </td>
  );
}

// Used by parent for typeahead refresh after approve. Re-export the
// extraction draft loader to avoid a second module hop in callers wanting
// to revalidate.
export { getListeningExtractionDraft };

/**
 * Owns its own draft text so each keystroke does not re-render the parent
 * panel. Without this isolation, controlled state on the parent fights
 * `userEvent.type` in JSDOM (the panel re-render races the keystroke and
 * the cursor / value gets clobbered after the first character).
 */
function RejectForm({
  acting,
  maxChars,
  onCancel,
  onConfirm,
}: {
  acting: boolean;
  maxChars: number;
  onCancel: () => void;
  onConfirm: (reason: string) => void;
}) {
  const [reason, setReason] = useState('');
  const trimmed = reason.trim();
  return (
    <div className="space-y-4">
      <p className="text-sm text-muted">
        Provide a reason (required, ≤ {maxChars} chars). This is recorded on the audit log so
        reviewers can see why proposals were turned down.
      </p>
      <Textarea
        label="Rejection reason"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        placeholder="e.g. Wrong question count for Part B; AI hallucinated stems."
        rows={4}
        maxLength={maxChars}
        hint={`${reason.length}/${maxChars}`}
      />
      <div className="flex items-center justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={onCancel} disabled={acting}>
          Cancel
        </Button>
        <Button
          variant="destructive"
          size="sm"
          onClick={() => onConfirm(trimmed)}
          loading={acting}
          disabled={acting || trimmed.length === 0}
        >
          Confirm reject
        </Button>
      </div>
    </div>
  );
}
