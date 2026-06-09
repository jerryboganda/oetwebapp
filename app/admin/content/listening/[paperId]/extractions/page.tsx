'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, CheckCircle2, FileText, Sparkles, Wand2, X } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { ListeningQuestionPaperViewer } from '@/components/domain/listening/ListeningQuestionPaperViewer';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getContentPaper, type ContentPaperDto } from '@/lib/content-upload-api';
import {
  approveListeningExtraction,
  getListeningExtractionDraft,
  listListeningExtractions,
  rejectListeningExtraction,
  runListeningExtraction,
  type ListeningExtractionDraftDetail,
  type ListeningExtractionDraftSummary,
} from '@/lib/listening-authoring-api';

type LoadState = 'loading' | 'ready' | 'error';

function firstParam(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'detail' in err) {
    const detail = (err as { detail?: unknown }).detail;
    if (detail && typeof detail === 'object') {
      const msg = (detail as { message?: unknown }).message;
      if (typeof msg === 'string' && msg.trim()) return msg;
    }
  }
  return err instanceof Error && err.message ? err.message : fallback;
}

export default function AdminListeningExtractionsPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId) ?? '';
  const { isAuthenticated, role } = useAdminAuth();

  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [error, setError] = useState<string | null>(null);
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [drafts, setDrafts] = useState<ListeningExtractionDraftSummary[]>([]);
  const [selected, setSelected] = useState<ListeningExtractionDraftDetail | null>(null);
  const [running, setRunning] = useState(false);
  const [deciding, setDeciding] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoadState('loading');
    try {
      const [paperData, draftList] = await Promise.all([
        getContentPaper(paperId),
        listListeningExtractions(paperId),
      ]);
      setPaper(paperData);
      setDrafts(draftList.drafts);
      setLoadState('ready');
    } catch (e) {
      setError(errorMessage(e, 'Could not load extraction data.'));
      setLoadState('error');
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  // Resolve the source question-paper PDF (prefer Part "A", else whole-paper).
  const questionPaperUrl = useMemo(() => {
    const assets = (paper?.assets ?? []).filter((a) => a.role === 'QuestionPaper' && a.media);
    const chosen =
      assets.find((a) => (a.part ?? '').toUpperCase() === 'A') ??
      assets.find((a) => !a.part) ??
      assets[0];
    return chosen?.media ? `/v1/media/${chosen.media.id}/content` : null;
  }, [paper?.assets]);

  const hasQuestionPaper = useMemo(
    () => (paper?.assets ?? []).some((a) => a.role === 'QuestionPaper' && a.media),
    [paper?.assets],
  );
  const hasAnswerKey = useMemo(
    () => (paper?.assets ?? []).some((a) => a.role === 'AnswerKey' && a.media),
    [paper?.assets],
  );
  const canRun = hasQuestionPaper && hasAnswerKey && !running;

  const openDraft = useCallback(async (draftId: string) => {
    try {
      const detail = await getListeningExtractionDraft(paperId, draftId);
      setSelected(detail);
    } catch (e) {
      setToast({ variant: 'error', message: errorMessage(e, 'Could not load the draft.') });
    }
  }, [paperId]);

  const onRun = useCallback(async () => {
    setRunning(true);
    setSelected(null);
    try {
      const result = await runListeningExtraction(paperId);
      setToast({ variant: 'success', message: result.summary });
      const draftList = await listListeningExtractions(paperId);
      setDrafts(draftList.drafts);
      await openDraft(result.draftId);
    } catch (e) {
      setToast({ variant: 'error', message: errorMessage(e, 'AI extraction failed.') });
    } finally {
      setRunning(false);
    }
  }, [paperId, openDraft]);

  const onApprove = useCallback(async () => {
    if (!selected) return;
    setDeciding(true);
    try {
      const result = await approveListeningExtraction(paperId, selected.id);
      const ready = result.report?.isPublishReady;
      setToast({
        variant: 'success',
        message: ready
          ? 'Approved and imported — structure is publish-ready.'
          : 'Approved and imported. Review the validation report before publishing.',
      });
      setSelected(null);
      const draftList = await listListeningExtractions(paperId);
      setDrafts(draftList.drafts);
    } catch (e) {
      setToast({ variant: 'error', message: errorMessage(e, 'Could not approve the draft.') });
    } finally {
      setDeciding(false);
    }
  }, [paperId, selected]);

  const onReject = useCallback(async () => {
    if (!selected) return;
    setDeciding(true);
    try {
      await rejectListeningExtraction(paperId, selected.id);
      setToast({ variant: 'success', message: 'Draft rejected.' });
      setSelected(null);
      const draftList = await listListeningExtractions(paperId);
      setDrafts(draftList.drafts);
    } catch (e) {
      setToast({ variant: 'error', message: errorMessage(e, 'Could not reject the draft.') });
    } finally {
      setDeciding(false);
    }
  }, [paperId, selected]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: `Paper ${paperId}`, href: `/admin/content/listening/${paperId}/structure` },
    { label: 'AI extraction' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: AI extraction" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Authoring"
      icon={<Sparkles className="h-5 w-5" />}
      title="Listening: AI data entry (Part A)"
      description={`Paper ${paperId}. Feed the question-paper + answer-key PDFs to Mistral OCR + Claude to auto-build the Part A note-completion and answers. Every draft is reviewed before it replaces the structure.`}
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/admin/content/listening/${paperId}/structure`}>
            <ArrowLeft className="h-4 w-4 mr-1.5" />
            Back to structure
          </Link>
        </Button>
      }
    >
      {loadState === 'loading' && <Skeleton className="h-64 rounded-admin" />}
      {loadState === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {loadState === 'ready' && (
        <div className="space-y-6">
          {/* Run panel */}
          <Card>
            <CardHeader><CardTitle>Run AI extraction</CardTitle></CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-2 sm:grid-cols-2">
                <StatusRow label="Question-paper PDF" ok={hasQuestionPaper} />
                <StatusRow label="Answer-key PDF" ok={hasAnswerKey} />
              </div>
              {(!hasQuestionPaper || !hasAnswerKey) && (
                <InlineAlert variant="warning">
                  Upload both the question-paper PDF and the answer-key PDF on the{' '}
                  <Link className="font-semibold underline" href={`/admin/content/listening/${paperId}/pdfs`}>PDFs tab</Link>{' '}
                  before running AI extraction.
                </InlineAlert>
              )}
              <div className="flex items-center gap-3">
                <Button variant="primary" onClick={onRun} disabled={!canRun} loading={running} loadingText="Extracting… (~20–40s)">
                  <Wand2 className="h-4 w-4 mr-1.5" />
                  Run AI extraction
                </Button>
                <p className="text-xs text-admin-fg-muted">OCR (Mistral) + structuring (Claude). Produces a reviewable draft — nothing is published automatically.</p>
              </div>
            </CardContent>
          </Card>

          {/* Pending drafts */}
          <Card>
            <CardHeader><CardTitle>Pending drafts</CardTitle></CardHeader>
            <CardContent>
              {drafts.length === 0 ? (
                <p className="text-sm text-admin-fg-muted">No pending drafts. Run an extraction to create one.</p>
              ) : (
                <ul className="space-y-2">
                  {drafts.map((d) => (
                    <li
                      key={d.id}
                      className="flex flex-wrap items-center justify-between gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-3"
                    >
                      <div className="min-w-0">
                        <p className="text-sm font-semibold text-admin-fg-strong">{d.summary}</p>
                        {d.isStub && d.stubReason && (
                          <p className="mt-1 text-xs text-[var(--admin-danger)]">⚠ {d.stubReason}</p>
                        )}
                      </div>
                      <Button
                        variant={selected?.id === d.id ? 'primary' : 'outline'}
                        size="sm"
                        onClick={() => void openDraft(d.id)}
                      >
                        Review
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>

          {/* Review panel */}
          {selected && (
            <Card>
              <CardHeader><CardTitle>Review draft</CardTitle></CardHeader>
              <CardContent className="space-y-5">
                {selected.isStub && selected.stubReason && (
                  <InlineAlert variant="warning">{selected.stubReason}</InlineAlert>
                )}

                <div className="grid gap-6 lg:grid-cols-2 lg:items-start">
                  {/* Left: source PDF reference */}
                  <div className="lg:sticky lg:top-4">
                    <p className="mb-2 text-xs font-black uppercase tracking-widest text-admin-fg-muted">Source question paper</p>
                    {questionPaperUrl ? (
                      <ListeningQuestionPaperViewer url={questionPaperUrl} partLabel="A" />
                    ) : (
                      <p className="text-sm text-admin-fg-muted">No question-paper PDF to preview.</p>
                    )}
                  </div>

                  {/* Right: proposed notes + answers */}
                  <div className="space-y-6">
                    {selected.extracts.map((extract) => {
                      const previewQuestions = extract.answers.map((a) => ({ id: `q-${a.number}`, number: a.number }));
                      return (
                        <div key={extract.partCode} className="space-y-3">
                          <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">
                            Part {extract.partCode} — proposed notes ({extract.gapCount} gaps)
                          </p>
                          <PartANotesDocument
                            partLabel={`Part ${extract.partCode}`}
                            notesBody={extract.notesBody ?? ''}
                            questions={previewQuestions}
                            answers={{}}
                            onAnswerChange={() => {}}
                            locked
                          />
                          <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                            <p className="mb-2 text-xs font-black uppercase tracking-widest text-admin-fg-muted">Proposed answers</p>
                            <ul className="space-y-1 text-sm">
                              {extract.answers.map((a) => (
                                <li key={a.number} className="flex flex-wrap items-baseline gap-2">
                                  <span className="font-semibold text-admin-fg-strong">({a.number})</span>
                                  <span className="text-admin-fg-strong">{a.correctAnswer || <em className="text-[var(--admin-danger)]">missing</em>}</span>
                                  {a.acceptedAnswers.length > 0 && (
                                    <span className="text-xs text-admin-fg-muted">— also: {a.acceptedAnswers.join(', ')}</span>
                                  )}
                                </li>
                              ))}
                            </ul>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>

                {/* Decision bar */}
                <div className="flex items-center justify-end gap-3 border-t border-admin-border pt-4">
                  <Button variant="outline" onClick={onReject} disabled={deciding}>
                    <X className="h-4 w-4 mr-1.5" />
                    Reject
                  </Button>
                  <Button variant="primary" onClick={onApprove} disabled={deciding} loading={deciding} loadingText="Importing…">
                    <CheckCircle2 className="h-4 w-4 mr-1.5" />
                    Approve &amp; import
                  </Button>
                </div>
              </CardContent>
            </Card>
          )}
        </div>
      )}

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}

function StatusRow({ label, ok }: { label: string; ok: boolean }) {
  return (
    <div className="flex items-center gap-2 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2">
      {ok ? (
        <CheckCircle2 className="h-4 w-4 text-[var(--admin-success)]" aria-hidden="true" />
      ) : (
        <FileText className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
      )}
      <span className="text-sm text-admin-fg-strong">{label}</span>
      <span className="ml-auto text-xs font-semibold text-admin-fg-muted">{ok ? 'Ready' : 'Missing'}</span>
    </div>
  );
}
