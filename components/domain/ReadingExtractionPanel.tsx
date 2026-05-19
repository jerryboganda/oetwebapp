'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, RefreshCw, Sparkles, XCircle } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Textarea } from '@/components/ui/form-controls';
import {
  approveReadingExtractionDraft,
  getReadingStructureAdmin,
  listReadingExtractionDrafts,
  proposeReadingStructure,
  rejectReadingExtractionDraft,
  type ReadingExtractionDraftDto,
  type ReadingExtractionStatus,
  type ReadingStructureManifestDto,
} from '@/lib/reading-authoring-api';

const REJECT_REASON_MAX = 500;

export interface ReadingExtractionPanelProps {
  paperId: string;
  onApplied?: () => void;
}

type ToastState = { variant: 'success' | 'error' | 'info'; message: string } | null;

interface ApiError extends Error {
  status?: number;
  detail?: { error?: string } | unknown;
}

function readApiError(e: unknown, fallback: string): string {
  const err = e as ApiError;
  const detail = err?.detail as { error?: string } | undefined;
  return detail?.error ?? err?.message ?? fallback;
}

function statusBadge(status: ReadingExtractionStatus, isStub: boolean) {
  if (status === 'Pending') return <Badge variant={isStub ? 'muted' : 'warning'}>{isStub ? 'Stub - Pending' : 'Pending'}</Badge>;
  if (status === 'Approved') return <Badge variant="success">Approved</Badge>;
  if (status === 'Failed') return <Badge variant="danger">Failed</Badge>;
  return <Badge variant="danger">Rejected</Badge>;
}

function countManifest(manifest: ReadingStructureManifestDto | null) {
  const empty = { texts: 0, questions: 0, partA: 0, partB: 0, partC: 0 };
  if (!manifest) return empty;
  return manifest.parts.reduce((acc, part) => {
    acc.texts += part.texts.length;
    acc.questions += part.questions.length;
    if (part.partCode === 'A') acc.partA = part.questions.length;
    if (part.partCode === 'B') acc.partB = part.questions.length;
    if (part.partCode === 'C') acc.partC = part.questions.length;
    return acc;
  }, empty);
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function ReadingExtractionPanel({ paperId, onApplied }: ReadingExtractionPanelProps) {
  const [drafts, setDrafts] = useState<ReadingExtractionDraftDto[]>([]);
  const [currentManifest, setCurrentManifest] = useState<ReadingStructureManifestDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [proposing, setProposing] = useState(false);
  const [acting, setActing] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [toast, setToast] = useState<ToastState>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const [allDrafts, current] = await Promise.all([
        listReadingExtractionDrafts(paperId),
        getReadingStructureAdmin(paperId),
      ]);
      setDrafts(allDrafts);
      setCurrentManifest({
        parts: current.parts.map((part) => ({
          partCode: part.partCode,
          timeLimitMinutes: part.timeLimitMinutes,
          instructions: part.instructions,
          texts: part.texts.map((text) => ({
            displayOrder: text.displayOrder,
            title: text.title,
            source: text.source,
            bodyHtml: text.bodyHtml,
            wordCount: text.wordCount,
            topicTag: text.topicTag,
          })),
          questions: part.questions.map((question) => ({
            displayOrder: question.displayOrder,
            points: question.points,
            questionType: question.questionType,
            stem: question.stem,
            optionsJson: question.optionsJson,
            correctAnswerJson: question.correctAnswerJson,
            acceptedSynonymsJson: question.acceptedSynonymsJson,
            caseSensitive: question.caseSensitive,
            explanationMarkdown: question.explanationMarkdown,
            skillTag: question.skillTag,
            readingTextDisplayOrder: null,
            optionDistractorsJson: question.optionDistractorsJson ?? null,
            reviewState: question.reviewState,
          })),
        })),
      });
    } catch (e) {
      setToast({ variant: 'error', message: readApiError(e, 'Failed to load Reading extraction state.') });
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const activeDraft = useMemo(
    () => drafts.find((draft) => draft.status === 'Pending') ?? drafts[0] ?? null,
    [drafts],
  );
  const proposedCounts = countManifest(activeDraft?.manifest ?? null);
  const currentCounts = countManifest(currentManifest);
  const canReject = Boolean(activeDraft && activeDraft.status === 'Pending' && rejectReason.trim().length > 0 && rejectReason.trim().length <= REJECT_REASON_MAX);
  const canApprove = Boolean(activeDraft && activeDraft.status === 'Pending' && !activeDraft.isStub && activeDraft.manifest);

  const propose = useCallback(async () => {
    setProposing(true);
    try {
      const draft = await proposeReadingStructure(paperId);
      setToast({
        variant: draft.isStub ? 'info' : 'success',
        message: draft.isStub
          ? `Stub draft saved. ${draft.notes ?? 'Fix source text or provider configuration, then re-run extraction.'}`
          : 'Grounded Reading extraction draft saved. Review before approving.',
      });
      await refresh();
    } catch (e) {
      setToast({ variant: 'error', message: readApiError(e, 'Reading extraction failed.') });
    } finally {
      setProposing(false);
    }
  }, [paperId, refresh]);

  const approve = useCallback(async () => {
    if (!activeDraft) return;
    setActing(true);
    try {
      await approveReadingExtractionDraft(paperId, activeDraft.id);
      setToast({ variant: 'success', message: 'Reading draft approved and structure replaced.' });
      await refresh();
      onApplied?.();
    } catch (e) {
      setToast({ variant: 'error', message: readApiError(e, 'Approve failed.') });
    } finally {
      setActing(false);
    }
  }, [activeDraft, onApplied, paperId, refresh]);

  const reject = useCallback(async () => {
    if (!activeDraft || !canReject) return;
    setActing(true);
    try {
      await rejectReadingExtractionDraft(paperId, activeDraft.id, rejectReason.trim());
      setRejectReason('');
      setToast({ variant: 'success', message: 'Reading draft rejected and audit logged.' });
      await refresh();
    } catch (e) {
      setToast({ variant: 'error', message: readApiError(e, 'Reject failed.') });
    } finally {
      setActing(false);
    }
  }, [activeDraft, canReject, paperId, refresh, rejectReason]);

  return (
    <AdminRoutePanel
      title="Reading AI extraction"
      description="Generate a grounded Reading structure draft from extracted paper text, then approve or reject it explicitly."
      actions={(
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={refresh} disabled={loading}>
            <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
            Refresh
          </Button>
          <Button variant="primary" size="sm" onClick={propose} disabled={proposing || acting}>
            {proposing ? <RefreshCw className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" /> : <Sparkles className="mr-2 h-4 w-4" aria-hidden="true" />}
            {proposing ? 'Extracting...' : 'Propose with AI'}
          </Button>
        </div>
      )}
    >
      {activeDraft ? (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-background-light p-4">
            <div>
              <div className="flex items-center gap-2">
                {statusBadge(activeDraft.status, activeDraft.isStub)}
                <span className="text-xs text-muted">Created {formatDate(activeDraft.createdAt)}</span>
              </div>
              {activeDraft.notes ? <p className="mt-2 text-sm text-muted">{activeDraft.notes}</p> : null}
            </div>
            <div className="text-sm text-muted">
              Current: {currentCounts.questions}/42 items · Draft: {proposedCounts.questions}/42 items
            </div>
          </div>

          {activeDraft.isStub ? (
            <InlineAlert
              variant="warning"
              title="Stub draft cannot be approved"
            >
              This draft is a placeholder produced because source text or AI provider configuration was unavailable. Reject it or re-run extraction after fixing the blocker.
            </InlineAlert>
          ) : null}

          <div className="grid gap-3 md:grid-cols-2">
            <div className="rounded-2xl border border-border p-4">
              <h3 className="text-sm font-semibold text-navy">Current structure</h3>
              <p className="mt-2 text-sm text-muted">A {currentCounts.partA} · B {currentCounts.partB} · C {currentCounts.partC} · texts {currentCounts.texts}</p>
            </div>
            <div className="rounded-2xl border border-border p-4">
              <h3 className="text-sm font-semibold text-navy">Proposed draft</h3>
              <p className="mt-2 text-sm text-muted">A {proposedCounts.partA} · B {proposedCounts.partB} · C {proposedCounts.partC} · texts {proposedCounts.texts}</p>
            </div>
          </div>

          <Textarea
            label="Rejection reason"
            value={rejectReason}
            onChange={(event) => setRejectReason(event.target.value.slice(0, REJECT_REASON_MAX))}
            placeholder="Explain what the AI draft got wrong before rejecting it."
          />

          <div className="flex flex-wrap gap-2">
            <Button variant="primary" onClick={approve} disabled={!canApprove || acting}>
              <CheckCircle2 className="mr-2 h-4 w-4" aria-hidden="true" />
              Approve & apply
            </Button>
            <Button variant="outline" onClick={reject} disabled={!canReject || acting}>
              <XCircle className="mr-2 h-4 w-4" aria-hidden="true" />
              Reject
            </Button>
          </div>
        </div>
      ) : (
        <InlineAlert
          variant="info"
          title="No Reading extraction draft yet"
        >
          Run a grounded AI extraction after uploading and extracting source text for the Reading paper.
        </InlineAlert>
      )}
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRoutePanel>
  );
}

export default ReadingExtractionPanel;
