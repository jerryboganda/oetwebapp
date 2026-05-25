'use client';

import { use, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Papers', href: '/admin/content/papers' },
    { label: 'Preview' },
  ];

  if (isAdminLoading || isUserLoading) return null;
  if (!isAuthenticated || role !== 'admin') return null;
  if (!canViewContent) return (
    <AdminSettingsLayout title="Paper Preview" breadcrumbs={breadcrumbs}>
      <InlineAlert variant="error">You do not have permission to view paper content.</InlineAlert>
    </AdminSettingsLayout>
  );

  return (
    <AdminSettingsLayout
      eyebrow="CMS"
      title="Paper Preview"
      description="Read-only view — this is how learners see the paper."
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/admin/content/papers/${paperId}`}>
            <ArrowLeft className="w-4 h-4 mr-1.5" />
            Back to Editor
          </Link>
        </Button>
      }
    >
      <InlineAlert variant="warning">
        Admin Preview — This is how learners see the paper. Not scored.
      </InlineAlert>

      <AsyncStateWrapper status={status} errorMessage={error ?? undefined} onRetry={load}>
        {structure && (
          <div className="space-y-6">
            {structure.parts.map((part) => (
              <Card key={part.partCode}>
                <CardHeader>
                  <CardTitle>Part {part.partCode}</CardTitle>
                  {part.instructions ? <p className="text-sm text-admin-fg-muted">{part.instructions}</p> : null}
                </CardHeader>
                <CardContent>
                <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
                  {/* Left: passages */}
                  <div className="space-y-6">
                    <h3 className="text-xs font-black uppercase tracking-[0.18em] text-admin-fg-muted">Passages</h3>
                    {part.texts.map((text) => (
                      <article key={text.id} className="space-y-2">
                        <h4 className="text-base font-bold text-admin-fg-strong">{text.title}</h4>
                        {text.source ? <p className="text-xs text-admin-fg-muted">Source: {text.source}</p> : null}
                        <div
                          className="prose prose-sm max-w-none rounded-admin border border-admin-border bg-admin-bg-subtle p-4 text-admin-fg-strong"
                          dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml) }}
                        />
                      </article>
                    ))}
                  </div>

                  {/* Right: questions */}
                  <div className="space-y-4">
                    <h3 className="text-xs font-black uppercase tracking-[0.18em] text-admin-fg-muted">Questions (answers highlighted)</h3>
                    {[...part.questions].sort((a, b) => a.displayOrder - b.displayOrder).map((question) => {
                      let options: { key: string; label: string }[] = [];
                      let correctKeys: string[] = [];
                      try { options = JSON.parse(question.optionsJson ?? '[]'); } catch { /* ignore */ }
                      try {
                        const raw = JSON.parse(question.correctAnswerJson ?? 'null');
                        correctKeys = Array.isArray(raw) ? raw.map(String) : raw != null ? [String(raw)] : [];
                      } catch { /* ignore */ }

                      return (
                        <div key={question.id} className="rounded-admin border border-admin-border bg-admin-bg-surface p-4 space-y-3">
                          <p className="text-sm font-semibold text-admin-fg-strong">{question.displayOrder}. {question.stem}</p>

                          {(question.questionType === 'MultipleChoice3' || question.questionType === 'MultipleChoice4') && (
                            <div className="space-y-1.5">
                              {options.map((opt) => {
                                const isCorrect = correctKeys.includes(opt.key);
                                return (
                                  <div
                                    key={opt.key}
                                    className={`flex items-center gap-2 rounded-admin border px-3 py-2 text-sm ${
                                      isCorrect ? 'border-emerald-500/50 bg-emerald-50 text-emerald-800' : 'border-admin-border bg-admin-bg-subtle text-admin-fg-strong'
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
                                className="w-full rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm text-admin-fg-muted cursor-not-allowed"
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
                                    className={`w-full rounded-admin border px-3 py-2 text-left text-sm cursor-not-allowed ${
                                      isCorrect ? 'border-emerald-500/50 bg-emerald-50 text-emerald-800' : 'border-admin-border bg-admin-bg-subtle text-admin-fg-strong opacity-70'
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
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </AsyncStateWrapper>
    </AdminSettingsLayout>
  );
}
