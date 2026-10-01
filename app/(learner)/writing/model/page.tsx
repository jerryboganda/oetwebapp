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
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
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

  const backToWriting = (
    <Button asChild variant="outline">
      <Link href="/writing">Back to writing</Link>
    </Button>
  );

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="list" />
      </>
    );
  }

  if (!model) {
    // The header always renders, so a gated or missing answer still says where the learner is.
    return (
      <LearnerPageHero
        eyebrow="Study Guide"
        icon={FileCheck}
        accent="primary"
        title="Model Answer Explainer"
        description={error ?? 'Model answer not found.'}
        aside={backToWriting}
      />
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow="Study Guide"
        icon={FileCheck}
        accent="primary"
        title="Model Answer Explainer"
        description={model.taskTitle}
        highlights={[
          { icon: Stethoscope, label: 'Profession', value: model.profession },
        ]}
        aside={backToWriting}
      />

      <MotionSection>
        <Card padding="lg">
          <h2 className="text-lg font-bold text-navy sm:text-xl">Why this is a strong response</h2>
          <p className="mt-2 max-w-3xl text-sm leading-relaxed text-muted sm:text-base">
            This model answer demonstrates a high-scoring response. Below, the letter is broken down paragraph by paragraph. Review the annotations to understand the <strong>rationale</strong>, <strong>scoring criteria</strong>, <strong>included / excluded details</strong>, and <strong>profession-specific language</strong> choices.
          </p>
        </Card>
      </MotionSection>

      <section className="space-y-6 sm:space-y-10">
        <LearnerSurfaceSectionHeader
          eyebrow="Paragraph Analysis"
          title="Annotated breakdown"
        />

        {model.paragraphs.map((paragraph, index) => (
          <MotionItem key={paragraph.id} delayIndex={Math.min(index, 5)} className="flex flex-col gap-4 lg:flex-row lg:gap-8">

            {/* Start: Paragraph text */}
            <div className="w-full shrink-0 lg:w-5/12">
              <div className="sticky top-24">
                <Card padding="lg" className="relative bg-background-light">
                  <div aria-hidden="true" className="absolute -start-3 -top-3 flex h-8 w-8 items-center justify-center rounded-full border-2 border-surface bg-primary text-sm font-bold tabular-nums text-white shadow-sm dark:bg-primary-700">{index + 1}</div>
                  {/* pre-line keeps in-paragraph line breaks (salutation / Re: line, address blocks). */}
                  <p className="whitespace-pre-line font-serif text-base leading-relaxed text-navy sm:text-lg">{paragraph.text}</p>
                </Card>
              </div>
            </div>

            {/* End: Annotations */}
            <div className="w-full min-w-0 space-y-4 lg:w-7/12">
              {/* Rationale */}
              <Card>
                <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                  <h3 className="flex items-center gap-2 text-sm font-bold text-navy"><BookOpen className="h-4 w-4 text-primary" aria-hidden="true" /> Rationale</h3>
                  <div className="flex flex-wrap gap-2">
                    {paragraph.criteria.map(crit => (
                      <Badge key={crit} variant="muted" size="sm"><Target className="me-1 inline h-3 w-3" aria-hidden="true" />{crit}</Badge>
                    ))}
                  </div>
                </div>
                <p className="text-sm leading-relaxed text-navy">{paragraph.rationale}</p>
              </Card>

              {/* Include / Exclude */}
              <Card>
                <h3 className="mb-4 text-sm font-bold text-navy">Include / Exclude Logic</h3>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div className="min-w-0">
                    <h4 className="eyebrow mb-2 flex items-center gap-1.5 text-success-strong"><CheckCircle2 className="h-4 w-4" aria-hidden="true" /> Included</h4>
                    <ul className="space-y-2">{paragraph.included.map((item, i) => (<li key={i} className="flex items-start gap-2 text-sm text-navy"><span aria-hidden="true" className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-success" /><span className="leading-snug">{item}</span></li>))}</ul>
                  </div>
                  {paragraph.excluded.length > 0 && (
                    <div className="min-w-0">
                      <h4 className="eyebrow mb-2 flex items-center gap-1.5 text-danger-strong"><XCircle className="h-4 w-4" aria-hidden="true" /> Excluded</h4>
                      <ul className="space-y-2">{paragraph.excluded.map((item, i) => (<li key={i} className="flex items-start gap-2 text-sm text-navy"><span aria-hidden="true" className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-danger" /><span className="leading-snug">{item}</span></li>))}</ul>
                    </div>
                  )}
                </div>
              </Card>

              {/* Language Notes */}
              <Card className="border-info/30 bg-info/10">
                <h3 className="mb-2 flex items-center gap-2 text-sm font-bold text-info"><Stethoscope className="h-4 w-4" aria-hidden="true" /> {model.profession} Language Notes</h3>
                <p className="text-sm leading-relaxed text-info">{paragraph.languageNotes}</p>
              </Card>
            </div>
          </MotionItem>
        ))}
      </section>
    </>
  );
}
