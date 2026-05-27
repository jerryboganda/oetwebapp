'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { RefreshCw } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { WritingEditorV2, type WritingEditorAnnotation } from '@/components/domain/writing/WritingEditorV2';
import { WordCounter } from '@/components/domain/writing/WordCounter';
import { SubmitBar } from '@/components/domain/writing/SubmitBar';
import { CanonViolationCard } from '@/components/domain/writing/CanonViolationCard';
import {
  getWritingSubmission,
  getWritingSubmissionGrade,
  reviseWritingSubmission,
} from '@/lib/writing/api';
import type {
  WritingGradeDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

export default function WritingReviseSubmissionPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const submissionId = String(params?.id ?? '');

  const [original, setOriginal] = useState<WritingSubmissionDto | null>(null);
  const [grade, setGrade] = useState<WritingGradeDto | null>(null);
  const [content, setContent] = useState('');
  const [wordCount, setWordCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const startedAtRef = useRef<number>(Date.now());

  useEffect(() => {
    if (!submissionId) return;
    void Promise.all([getWritingSubmission(submissionId), getWritingSubmissionGrade(submissionId)])
      .then(([s, g]) => {
        setOriginal(s);
        setGrade(g);
        setContent(s.letterContent);
        setWordCount(s.wordCount);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load original letter.'));
  }, [submissionId]);

  const annotations = useMemo<WritingEditorAnnotation[]>(() => {
    if (!grade?.canonViolations) return [];
    return grade.canonViolations.map((v) => ({
      charStart: v.charStart,
      charEnd: v.charEnd,
      type: 'canon',
      note: `${v.ruleId}: ${v.suggestedFix ?? v.ruleText}`,
      ruleId: v.ruleId,
    }));
  }, [grade]);

  const canSubmit = wordCount >= 50 && !submitting && content !== original?.letterContent;

  const helperText = wordCount < 50
    ? 'Letter is too short to submit.'
    : content === original?.letterContent
      ? 'Make at least one change before resubmitting.'
      : 'Your revision will be graded separately and linked to the original.';

  const onSubmit = useCallback(async () => {
    if (!canSubmit || !original) return;
    setSubmitting(true);
    setError(null);
    try {
      const elapsed = Math.round((Date.now() - startedAtRef.current) / 1000);
      const revised = await reviseWritingSubmission(submissionId, {
        letterContent: content,
        wordCount,
        timeSpentSeconds: elapsed,
      });
      router.push(`/writing/submissions/${encodeURIComponent(revised.id)}/grading`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit revision.');
      setSubmitting(false);
    }
  }, [canSubmit, content, original, submissionId, wordCount, router]);

  return (
    <LearnerDashboardShell pageTitle="Revise letter" distractionFree>
      <div className="space-y-4 pb-32" aria-busy={!original}>
        <header className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm">
          <div className="flex items-center gap-3">
            <RefreshCw className="h-5 w-5 text-amber-600" aria-hidden="true" />
            <div>
              <p className="text-xs font-bold uppercase tracking-wider text-muted">Revision</p>
              <h1 className="text-base font-bold text-navy">{original ? `Revising your ${original.mode} submission` : 'Loading…'}</h1>
              {grade ? <p className="mt-1 text-xs text-muted">Original band: <Badge variant="muted" size="sm">{grade.bandLabel}</Badge></p> : null}
            </div>
          </div>
          <WordCounter count={wordCount} target={{ min: 180, max: 220 }} ariaLabelPrefix="Letter length" />
        </header>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {grade?.canonViolations?.length ? (
          <Card padding="md">
            <CardContent>
              <h2 className="text-sm font-bold text-navy">Issues to fix before resubmitting</h2>
              <div className="mt-2 grid gap-2 md:grid-cols-2">
                {grade.canonViolations.map((v) => (
                  <CanonViolationCard key={v.id} violation={v} />
                ))}
              </div>
            </CardContent>
          </Card>
        ) : null}

        <section aria-label="Revision editor" className="rounded-2xl border border-border bg-surface p-4">
          <WritingEditorV2
            mode="revision"
            initialContent={original?.letterContent ?? ''}
            annotations={annotations}
            onChange={(text, words) => {
              setContent(text);
              setWordCount(words);
            }}
            placeholder="Edit your original letter to address the feedback above."
            inputId="revision-editor"
          />
        </section>

        <SubmitBar
          canSubmit={canSubmit}
          submitLabel="Submit revision"
          onSubmit={() => void onSubmit()}
          loading={submitting}
          helperText={helperText}
        />
      </div>
    </LearnerDashboardShell>
  );
}
