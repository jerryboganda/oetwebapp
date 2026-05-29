'use client';

/**
 * Phase 4 closure — admin workflow for the AI-extracted Reading drafts.
 *
 * Three panes (migrated to admin design system — AdminOperationsLayout):
 *   1. Paper picker (left).
 *   2. Draft list (middle), with status badges + per-part counts.
 *   3. Draft detail (right): manifest preview + approve / reject CTAs.
 *
 * Wrappers `proposeReadingStructure`, `listReadingExtractionDrafts`,
 * `approveReadingExtractionDraft`, `rejectReadingExtractionDraft`
 * already exist in `lib/reading-authoring-api.ts:650-691`.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ChevronRight, FlaskConical, RefreshCw, Sparkles } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle, CardDescription, CardAction } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { InlineAlert } from '@/components/ui/alert';
import { cn } from '@/lib/utils';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  approveReadingExtractionDraft,
  getReadingAdminAnalytics,
  listReadingExtractionDrafts,
  proposeReadingStructure,
  rejectReadingExtractionDraft,
  type ReadingExtractionDraftDto,
} from '@/lib/reading-authoring-api';
import { ReadingExtractionDraftCard } from '@/components/domain/admin/ReadingExtractionDraftCard';
import { ReadingManifestPreview } from '@/components/domain/admin/ReadingManifestPreview';

interface PaperOption {
  id: string;
  title: string;
  status: string;
}

export default function AdminReadingExtractionPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [papers, setPapers] = useState<PaperOption[]>([]);
  const [selectedPaperId, setSelectedPaperId] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<ReadingExtractionDraftDto[]>([]);
  const [selectedDraftId, setSelectedDraftId] = useState<string | null>(null);
  const [loadingPapers, setLoadingPapers] = useState(false);
  const [loadingDrafts, setLoadingDrafts] = useState(false);
  const [proposing, setProposing] = useState(false);
  const [approvingId, setApprovingId] = useState<string | null>(null);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const isAdmin = isAuthenticated && role === 'admin';

  useEffect(() => {
    if (!isAdmin) return;
    let cancelled = false;
    (async () => {
      setLoadingPapers(true);
      try {
        const dto = await getReadingAdminAnalytics(90);
        if (cancelled) return;
        const opts = dto.papers.map((p) => ({ id: p.paperId, title: p.title, status: p.status }));
        setPapers(opts);
        setSelectedPaperId((current) => current ?? opts[0]?.id ?? null);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load Reading papers.');
      } finally {
        if (!cancelled) setLoadingPapers(false);
      }
    })();
    return () => { cancelled = true; };
  }, [isAdmin]);

  const refreshDrafts = useCallback(async (paperId: string) => {
    setLoadingDrafts(true);
    setError(null);
    try {
      const list = await listReadingExtractionDrafts(paperId);
      setDrafts(list);
      setSelectedDraftId((current) => {
        if (current && list.some((d) => d.id === current)) return current;
        return list[0]?.id ?? null;
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load drafts.');
    } finally {
      setLoadingDrafts(false);
    }
  }, []);

  useEffect(() => {
    if (!isAdmin || !selectedPaperId) return;
    void refreshDrafts(selectedPaperId);
  }, [isAdmin, selectedPaperId, refreshDrafts]);

  const handlePropose = useCallback(async () => {
    if (!selectedPaperId) return;
    setProposing(true);
    setError(null);
    setInfo(null);
    try {
      const created = await proposeReadingStructure(selectedPaperId);
      setInfo(`New draft ${created.id} created (${created.isStub ? 'stub' : 'AI'} mode).`);
      await refreshDrafts(selectedPaperId);
      setSelectedDraftId(created.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Propose failed.');
    } finally {
      setProposing(false);
    }
  }, [selectedPaperId, refreshDrafts]);

  const handleApprove = useCallback(async (draftId: string) => {
    if (!selectedPaperId) return;
    const confirmed = typeof window === 'undefined'
      ? true
      : window.confirm(
          'Approving will replace the existing Reading paper structure. Continue?',
        );
    if (!confirmed) return;
    setApprovingId(draftId);
    setError(null);
    setInfo(null);
    try {
      const updated = await approveReadingExtractionDraft(selectedPaperId, draftId);
      setInfo(`Draft ${updated.id} approved. Paper structure replaced.`);
      await refreshDrafts(selectedPaperId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Approve failed.');
    } finally {
      setApprovingId(null);
    }
  }, [selectedPaperId, refreshDrafts]);

  const handleReject = useCallback(async (draftId: string, reason: string) => {
    if (!selectedPaperId) return;
    setRejectingId(draftId);
    setError(null);
    setInfo(null);
    try {
      const updated = await rejectReadingExtractionDraft(selectedPaperId, draftId, reason);
      setInfo(`Draft ${updated.id} rejected. Reason recorded.`);
      await refreshDrafts(selectedPaperId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reject failed.');
    } finally {
      setRejectingId(null);
    }
  }, [selectedPaperId, refreshDrafts]);

  const selectedDraft = useMemo(
    () => drafts.find((d) => d.id === selectedDraftId) ?? null,
    [drafts, selectedDraftId],
  );

  if (!isAdmin) return null;

  return (
    <AdminOperationsLayout
      role="main"
      aria-label="Reading AI extraction"
      title="AI Extraction Drafts"
      description="Propose AI-extracted Reading questions, review the generated manifest, and approve or reject. Approval replaces the live paper structure; preview before confirming."
      eyebrow="Content / Reading"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Extraction' },
      ]}
      primaryGrid={
        <div className="space-y-4">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {info ? <InlineAlert variant="success">{info}</InlineAlert> : null}

          <div className="grid gap-4 lg:grid-cols-[16rem_22rem_minmax(0,1fr)]">
            {/* Pane 1 — Paper picker */}
            <Card>
              <CardHeader>
                <div className="min-w-0">
                  <CardTitle>Reading papers</CardTitle>
                  <CardDescription>Latest 90-day window from admin analytics.</CardDescription>
                </div>
              </CardHeader>
              <CardContent>
                {loadingPapers ? (
                  <div className="space-y-2">
                    <Skeleton className="h-8 w-full" />
                    <Skeleton className="h-8 w-full" />
                  </div>
                ) : papers.length === 0 ? (
                  <EmptyState
                    size="sm"
                    illustration={<Sparkles />}
                    title="No Reading papers found"
                    description="Once a Reading paper has activity in the last 90 days it will appear here."
                  />
                ) : (
                  <ul className="space-y-1">
                    {papers.map((paper) => {
                      const active = paper.id === selectedPaperId;
                      return (
                        <li key={paper.id}>
                          <button
                            type="button"
                            onClick={() => setSelectedPaperId(paper.id)}
                            className={cn(
                              'flex w-full items-center justify-between gap-2 rounded-admin border px-3 py-2 text-left text-sm transition-colors',
                              active
                                ? 'border-[var(--admin-primary)] bg-[var(--admin-state-selected)] text-admin-fg-strong'
                                : 'border-transparent text-admin-fg-default hover:border-admin-border hover:bg-[var(--admin-state-hover)]',
                            )}
                          >
                            <span className="min-w-0">
                              <span className="block truncate font-semibold">{paper.title}</span>
                              <span className="block text-xs text-admin-fg-muted">{paper.status}</span>
                            </span>
                            <ChevronRight className="h-4 w-4 text-admin-fg-muted" aria-hidden />
                          </button>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </CardContent>
            </Card>

            {/* Pane 2 — Drafts list */}
            <Card>
              <CardHeader>
                <div className="min-w-0">
                  <CardTitle>Drafts</CardTitle>
                  <CardDescription>Latest AI-extracted drafts for the selected paper.</CardDescription>
                </div>
                <CardAction>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    onClick={() => selectedPaperId && void refreshDrafts(selectedPaperId)}
                    disabled={!selectedPaperId || loadingDrafts}
                    aria-label="Refresh drafts"
                    className="h-8 w-8 min-w-0"
                  >
                    <RefreshCw className="h-4 w-4" aria-hidden />
                  </Button>
                  <Button
                    type="button"
                    variant="primary"
                    size="sm"
                    onClick={() => void handlePropose()}
                    disabled={!selectedPaperId || proposing}
                    startIcon={<FlaskConical className="h-4 w-4" aria-hidden />}
                  >
                    {proposing ? 'Proposing…' : 'New draft'}
                  </Button>
                </CardAction>
              </CardHeader>
              <CardContent>
                {loadingDrafts ? (
                  <div className="space-y-3">
                    <Skeleton className="h-24 w-full" />
                    <Skeleton className="h-24 w-full" />
                  </div>
                ) : drafts.length === 0 ? (
                  <EmptyState
                    size="sm"
                    illustration={<FlaskConical />}
                    title="No drafts yet"
                    description="Click New draft to invoke the AI extractor (or get a stub when the gateway is not configured)."
                    primaryAction={
                      selectedPaperId
                        ? { label: 'Propose draft', onClick: () => void handlePropose(), loading: proposing }
                        : undefined
                    }
                  />
                ) : (
                  <div className="space-y-3">
                    {drafts.map((draft) => (
                      <ReadingExtractionDraftCard
                        key={draft.id}
                        draft={draft}
                        isActive={draft.id === selectedDraftId}
                        isApproving={approvingId === draft.id}
                        isRejecting={rejectingId === draft.id}
                        onSelect={() => setSelectedDraftId(draft.id)}
                        onApprove={() => void handleApprove(draft.id)}
                        onReject={(reason) => void handleReject(draft.id, reason)}
                      />
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>

            {/* Pane 3 — Manifest preview */}
            <Card>
              <CardHeader>
                <div className="min-w-0">
                  <CardTitle>Manifest preview</CardTitle>
                  <CardDescription>
                    Read-only walk of the draft&apos;s parts, texts, and questions. Verify before approve.
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent>
                {!selectedDraft ? (
                  <p className="text-sm text-admin-fg-muted">Pick a draft to preview its manifest.</p>
                ) : !selectedDraft.manifest ? (
                  <div className="space-y-2">
                    <Badge variant="warning">No manifest</Badge>
                    <p className="text-sm text-admin-fg-muted">
                      This draft has no manifest payload. Likely the extractor failed; check the
                      draft notes and re-run.
                    </p>
                  </div>
                ) : (
                  <ReadingManifestPreview manifest={selectedDraft.manifest} />
                )}
              </CardContent>
            </Card>
          </div>
        </div>
      }
    />
  );
}
