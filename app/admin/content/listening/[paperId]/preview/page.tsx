'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, CheckCircle2, Eye, KeyRound, ShieldCheck } from 'lucide-react';
import { useParams } from 'next/navigation';

import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningCandidatePreview,
  getListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningCandidatePreview,
} from '@/lib/listening-authoring-api';

type PreviewMode = 'candidate' | 'marking';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function ListeningPreviewPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();
  const [mode, setMode] = useState<PreviewMode>('candidate');
  const [candidate, setCandidate] = useState<ListeningCandidatePreview | null>(null);
  const [marking, setMarking] = useState<ListeningAuthoredQuestion[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!paperId || !isAuthenticated || role !== 'admin') return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([getListeningCandidatePreview(paperId), getListeningStructure(paperId)])
      .then(([candidatePreview, markingPreview]) => {
        if (cancelled) return;
        setCandidate(candidatePreview);
        setMarking(markingPreview.questions);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load Listening previews.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [isAuthenticated, paperId, role]);

  const questions = useMemo(() => {
    if (mode === 'candidate') return candidate?.questions ?? [];
    return marking;
  }, [candidate?.questions, marking, mode]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Preview' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening preview" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Listening authoring"
      title="Preview before publish"
      description="Review the learner-facing paper and the answer-key marking view before publishing."
      icon={<Eye className="h-5 w-5" />}
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/admin/content/listening/${paperId}/structure`}>
            <ArrowLeft className="mr-1.5 h-4 w-4" />
            Back to structure
          </Link>
        </Button>
      }
    >
      <div className="space-y-6">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        {loading ? <Skeleton className="h-56 rounded-admin" /> : candidate ? (
          <>
            <Card>
              <CardHeader>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <CardTitle>{candidate.paper.title}</CardTitle>
                    <p className="mt-1 text-sm text-admin-fg-muted">
                      {candidate.paper.subtestCode} · {candidate.paper.estimatedDurationMinutes} minutes
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="info">{candidate.counts.totalItems} / 42 questions</Badge>
                    <Badge variant={mode === 'candidate' ? 'success' : 'warning'}>
                      {mode === 'candidate' ? 'Candidate view' : 'Marking view'}
                    </Badge>
                  </div>
                </div>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="flex flex-wrap gap-2" role="tablist" aria-label="Preview mode">
                  <Button
                    type="button"
                    variant={mode === 'candidate' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setMode('candidate')}
                    startIcon={<ShieldCheck className="h-4 w-4" />}
                  >
                    Candidate preview
                  </Button>
                  <Button
                    type="button"
                    variant={mode === 'marking' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setMode('marking')}
                    startIcon={<KeyRound className="h-4 w-4" />}
                  >
                    Marking preview
                  </Button>
                </div>

                <InlineAlert variant={mode === 'candidate' ? 'info' : 'warning'}>
                  {mode === 'candidate'
                    ? 'Candidate preview uses a server-side learner-safe projection. Correct answers, accepted variants, and explanations are not returned.'
                    : 'Marking preview is admin-only and shows the answer key for final authoring review. It is never a learner projection.'}
                </InlineAlert>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>{mode === 'candidate' ? 'Candidate paper' : 'Marking answer key'}</CardTitle></CardHeader>
              <CardContent className="space-y-3">
                {questions.length === 0 ? (
                  <p className="text-sm text-admin-fg-muted">No authored questions are available for preview.</p>
                ) : questions.map((question) => (
                  <PreviewQuestion
                    key={question.id}
                    question={question}
                    showAnswer={mode === 'marking'}
                  />
                ))}
              </CardContent>
            </Card>
          </>
        ) : null}
      </div>
    </AdminSettingsLayout>
  );
}

function PreviewQuestion({
  question,
  showAnswer,
}: {
  question: ListeningCandidatePreview['questions'][number] | ListeningAuthoredQuestion;
  showAnswer: boolean;
}) {
  const options = question.options ?? [];
  const answer = 'correctAnswer' in question ? question.correctAnswer : null;

  return (
    <article className="rounded-admin border border-admin-border bg-admin-bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex items-start gap-2">
          <span className="font-mono text-sm text-admin-fg-muted">Q{question.number}</span>
          <p className="text-sm font-semibold text-admin-fg-strong">{question.stem || 'Untitled question'}</p>
        </div>
        <Badge variant="muted">{question.partCode}</Badge>
      </div>

      {options.length > 0 && (
        <ul className="mt-3 space-y-1.5 pl-7">
          {options.map((option, index) => (
            <li key={`${question.id}-${index}`} className="text-sm text-admin-fg-default">
              <span className="mr-1 font-semibold">{String.fromCharCode(65 + index)}.</span>{option}
            </li>
          ))}
        </ul>
      )}

      {showAnswer && answer && (
        <p className="mt-3 flex items-center gap-1.5 text-sm font-semibold text-[var(--admin-success)]">
          <CheckCircle2 className="h-4 w-4" />
          Correct answer: {answer}
        </p>
      )}
    </article>
  );
}
