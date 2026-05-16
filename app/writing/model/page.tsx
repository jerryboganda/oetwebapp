'use client';

import { useState, useEffect } from 'react';
import {
  CheckCircle2,
  XCircle,
  BookOpen,
  Stethoscope,
  Target,
  FileCheck,
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchModelAnswer, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ModelAnswer } from '@/lib/mock-data';

type ModelAnswerState = {
  taskId: string;
  model: ModelAnswer | null;
  loading: boolean;
  error: string | null;
};

export default function ModelAnswerExplainer() {
  const searchParams = useSearchParams();
  const taskId = searchParams?.get('taskId') ?? '';
  const missingModelAnswerMessage = 'Model answer unavailable. Open a published Writing task first.';
  const [modelState, setModelState] = useState<ModelAnswerState>({
    taskId,
    model: null,
    loading: taskId.length > 0,
    error: null,
  });
  const isCurrentTask = modelState.taskId === taskId;
  const model = taskId && isCurrentTask ? modelState.model : null;
  const loading = taskId.length > 0 && (!isCurrentTask || modelState.loading);
  const error = !taskId ? missingModelAnswerMessage : isCurrentTask ? modelState.error : null;

  useEffect(() => {
    if (!taskId) {
      return;
    }
    let active = true;
    analytics.track('content_view', { content: 'model_answer', taskId, subtest: 'writing' });
    fetchModelAnswer(taskId)
      .then(answer => {
        if (active) setModelState({ taskId, model: answer, loading: false, error: null });
      })
      .catch((err: unknown) => {
        if (active) {
          const message = isApiError(err) && err.status === 404
            ? 'This model answer is no longer available.'
            : isApiError(err) && err.code === 'writing_model_answer_locked'
              ? 'Submit your Writing attempt before opening the model answer.'
              : 'We could not load this model answer. Please try again.';
          setModelState({
            taskId,
            model: null,
            loading: false,
            error: message,
          });
        }
      });
    return () => {
      active = false;
    };
  }, [taskId]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Model Answer Explainer">
        <div className="space-y-6">
          <Skeleton className="h-32 rounded-2xl" />
          {[1, 2, 3].map(i => <Skeleton key={i} className="h-48 rounded-2xl" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!model) {
    return (
      <LearnerDashboardShell pageTitle="Model Answer Explainer">
        <div className="p-6">
          <InlineAlert variant="warning">{error ?? 'Model answer not found.'}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Model Answer Explainer">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="Study Guide"
          icon={FileCheck}
          accent="primary"
          title="Model Answer Explainer"
          description={model.taskTitle}
          highlights={[
            { icon: Stethoscope, label: 'Profession', value: model.profession },
            { icon: BookOpen, label: 'Format', value: 'Annotated response' },
            { icon: Target, label: 'Goal', value: 'High-scoring writing' },
          ]}
          aside={
            <Link href="/writing" className="inline-flex items-center justify-center rounded-xl border border-border bg-surface px-4 py-2 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-background-light">
              Back to writing
            </Link>
          }
        />

        <MotionSection className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <h2 className="mb-3 text-2xl font-bold text-navy">Why this is a strong response</h2>
          <p className="max-w-3xl leading-relaxed text-muted">
            This model answer demonstrates a high-scoring response. Below, the letter is broken down paragraph by paragraph. Review the annotations to understand the <strong>rationale</strong>, <strong>scoring criteria</strong>, <strong>included / excluded details</strong>, and <strong>profession-specific language</strong> choices.
          </p>
        </MotionSection>

        <LearnerSurfaceSectionHeader
          eyebrow="Paragraph Analysis"
          title="Annotated breakdown"
          description="Each paragraph is paired with rationale, inclusion logic, and profession-specific language notes."
        />

        <div className="space-y-12">
          {model.paragraphs.map((paragraph, index) => (
            <MotionItem key={paragraph.id} delayIndex={index} className="flex flex-col lg:flex-row gap-6 lg:gap-8">

              {/* Left: Paragraph text */}
              <div className="w-full lg:w-5/12 shrink-0">
                <div className="sticky top-24">
                  <Card className="relative border-border bg-background-light p-6">
                    <div className="absolute -left-3 -top-3 w-8 h-8 bg-primary text-white rounded-full flex items-center justify-center font-bold text-sm shadow-sm border-2 border-white">{index + 1}</div>
                    <p className="text-lg leading-relaxed text-navy font-serif">{paragraph.text}</p>
                  </Card>
                </div>
              </div>

              {/* Right: Annotations */}
              <div className="w-full lg:w-7/12 space-y-4">
                {/* Rationale */}
                <Card className="border-border bg-surface p-5">
                  <div className="flex flex-wrap items-center justify-between gap-4 mb-3">
                    <h3 className="text-sm font-bold text-navy uppercase tracking-wider flex items-center gap-2"><BookOpen className="w-4 h-4 text-primary" /> Rationale</h3>
                    <div className="flex flex-wrap gap-2">
                      {paragraph.criteria.map(crit => (
                        <Badge key={crit} variant="muted" size="sm"><Target className="w-3 h-3 mr-1 inline" />{crit}</Badge>
                      ))}
                    </div>
                  </div>
                  <p className="text-navy leading-relaxed text-sm">{paragraph.rationale}</p>
                </Card>

                {/* Include / Exclude */}
                <Card className="border-border bg-surface p-5">
                  <h3 className="text-sm font-bold text-navy uppercase tracking-wider mb-4">Include / Exclude Logic</h3>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <h4 className="text-xs font-bold text-success uppercase tracking-wider mb-2 flex items-center gap-1.5"><CheckCircle2 className="w-4 h-4" /> Included</h4>
                      <ul className="space-y-2">{paragraph.included.map((item, i) => (<li key={i} className="text-sm text-navy flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-success mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                    </div>
                    {paragraph.excluded.length > 0 && (
                      <div>
                        <h4 className="text-xs font-bold text-danger uppercase tracking-wider mb-2 flex items-center gap-1.5"><XCircle className="w-4 h-4" /> Excluded</h4>
                        <ul className="space-y-2">{paragraph.excluded.map((item, i) => (<li key={i} className="text-sm text-navy flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-danger mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                      </div>
                    )}
                  </div>
                </Card>

                {/* Language Notes */}
                <div className="rounded-2xl border border-info/30 bg-info/10 p-5 shadow-sm">
                  <h3 className="text-sm font-bold text-info uppercase tracking-wider mb-2 flex items-center gap-2"><Stethoscope className="w-4 h-4" /> {model.profession} Language Notes</h3>
                  <p className="text-info leading-relaxed text-sm">{paragraph.languageNotes}</p>
                </div>
              </div>
            </MotionItem>
          ))}
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
