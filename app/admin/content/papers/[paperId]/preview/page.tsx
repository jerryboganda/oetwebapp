'use client';

import { use, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { InlineAlert } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { getReadingStructureAdmin, type ReadingStructureAdminDto } from '@/lib/reading-authoring-api';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

type PageStatus = 'loading' | 'success' | 'error';

export default function ReadingPaperPreviewPage({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const { isAuthenticated, isLoading: isAdminLoading, role } = useAdminAuth();
  const { user, isLoading: isUserLoading } = useCurrentUser();
  const canViewContent = hasPermission(user?.adminPermissions, AdminPermission.ContentRead)
    || hasPermission(user?.adminPermissions, AdminPermission.ContentWrite)
    || hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [status, setStatus] = useState<PageStatus>('loading');
  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!canViewContent) return;
    setStatus('loading');
    try {
      const s = await getReadingStructureAdmin(paperId);
      setStructure(s);
      setStatus('success');
    } catch (e) {
      setError((e as Error).message ?? 'Failed to load structure.');
      setStatus('error');
    }
  }, [canViewContent, paperId]);

  useEffect(() => {
    if (!canViewContent) return;
    queueMicrotask(() => { void load(); });
  }, [canViewContent, load]);

  if (isAdminLoading || isUserLoading) return null;
  if (!isAuthenticated || role !== 'admin') return null;
  if (!canViewContent) return (
    <AdminRouteWorkspace role="main">
      <InlineAlert variant="error">You do not have permission to view paper content.</InlineAlert>
    </AdminRouteWorkspace>
  );

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading paper preview">
      <AdminRouteSectionHeader
        title="Paper Preview"
        description="Read-only view — this is how learners see the paper."
        actions={
          <Link
            href={`/admin/content/papers/${paperId}`}
            className="inline-flex items-center gap-1.5 rounded-lg border border-admin-border bg-admin-surface px-3 py-1.5 text-sm font-semibold text-admin-text hover:bg-admin-surface-raised transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
            Back to Editor
          </Link>
        }
      />

      <InlineAlert variant="warning">
        Admin Preview — This is how learners see the paper. Not scored.
      </InlineAlert>

      <AsyncStateWrapper status={status} errorMessage={error ?? undefined} onRetry={load}>
        {structure && (
          <div className="space-y-6">
            {structure.parts.map((part) => (
              <AdminRoutePanel
                key={part.partCode}
                title={`Part ${part.partCode}`}
                description={part.instructions ?? undefined}
              >
                <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
                  {/* Left: passages */}
                  <div className="space-y-6">
                    <h3 className="text-xs font-black uppercase tracking-[0.18em] text-muted">Passages</h3>
                    {part.texts.map((text) => (
                      <article key={text.id} className="space-y-2">
                        <h4 className="text-base font-bold text-navy">{text.title}</h4>
                        {text.source ? <p className="text-xs text-muted">Source: {text.source}</p> : null}
                        <div
                          className="prose prose-sm max-w-none rounded-xl border border-border bg-background-light p-4 text-navy"
                          dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml) }}
                        />
                      </article>
                    ))}
                  </div>

                  {/* Right: questions */}
                  <div className="space-y-4">
                    <h3 className="text-xs font-black uppercase tracking-[0.18em] text-muted">Questions (answers highlighted)</h3>
                    {[...part.questions].sort((a, b) => a.displayOrder - b.displayOrder).map((question) => {
                      let options: { key: string; label: string }[] = [];
                      let correctKeys: string[] = [];
                      try { options = JSON.parse(question.optionsJson ?? '[]'); } catch { /* ignore */ }
                      try {
                        const raw = JSON.parse(question.correctAnswerJson ?? 'null');
                        correctKeys = Array.isArray(raw) ? raw.map(String) : raw != null ? [String(raw)] : [];
                      } catch { /* ignore */ }

                      return (
                        <div key={question.id} className="rounded-xl border border-border bg-surface p-4 space-y-3">
                          <p className="text-sm font-semibold text-navy">{question.displayOrder}. {question.stem}</p>

                          {(question.questionType === 'MultipleChoice3' || question.questionType === 'MultipleChoice4') && (
                            <div className="space-y-1.5">
                              {options.map((opt) => {
                                const isCorrect = correctKeys.includes(opt.key);
                                return (
                                  <div
                                    key={opt.key}
                                    className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm ${
                                      isCorrect ? 'border-emerald-500/50 bg-emerald-50 text-emerald-800' : 'border-border bg-background-light text-navy'
                                    }`}
                                    aria-disabled="true"
                                  >
                                    <span className="font-bold">{opt.key}.</span>
                                    <span>{opt.label}</span>
                                    {isCorrect && <span className="ml-auto text-xs font-bold text-emerald-700">&#x2713; Correct</span>}
                                  </div>
                                );
                              })}
                            </div>
                          )}

                          {(question.questionType === 'ShortAnswer' || question.questionType === 'SentenceCompletion') && (
                            <div className="space-y-1">
                              <input
                                type="text"
                                disabled
                                aria-disabled="true"
                                placeholder="Learner types answer here"
                                className="w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-muted cursor-not-allowed"
                              />
                              {correctKeys.length > 0 && (
                                <p className="text-sm font-semibold text-emerald-700">&#x2713; Correct: {correctKeys.join(' / ')}</p>
                              )}
                            </div>
                          )}

                          {question.questionType === 'MatchingTextReference' && (
                            <div className="space-y-1.5">
                              {options.map((opt) => {
                                const isCorrect = correctKeys.includes(opt.key);
                                return (
                                  <button
                                    key={opt.key}
                                    disabled
                                    aria-disabled="true"
                                    type="button"
                                    className={`w-full rounded-lg border px-3 py-2 text-left text-sm cursor-not-allowed ${
                                      isCorrect ? 'border-emerald-500/50 bg-emerald-50 text-emerald-800' : 'border-border bg-background-light text-navy opacity-70'
                                    }`}
                                  >
                                    <span className="font-bold">{opt.key}.</span> {opt.label}
                                    {isCorrect && <span className="ml-2 text-xs font-bold text-emerald-700">&#x2713;</span>}
                                  </button>
                                );
                              })}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              </AdminRoutePanel>
            ))}
          </div>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
