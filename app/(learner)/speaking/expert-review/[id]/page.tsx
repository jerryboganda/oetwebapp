'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams } from 'next/navigation';
import {
  Clock, CreditCard, MessageSquare, CheckCircle2,
  Target, ArrowRight, Sparkles,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
import { PageSkeleton } from '@/components/ui/skeleton';
import { fetchFocusAreas, fetchTurnaroundOptions, fetchBilling, isApiError, submitReviewRequest } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { TurnaroundOption } from '@/lib/mock-data';

function ExpertReviewRequestContent() {
  const params = useParams();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  // --- Data State ---
  const [focusAreas, setFocusAreas] = useState<{ id: string; label: string; description: string }[]>([]);
  const [turnaroundOptions, setTurnaroundOptions] = useState<TurnaroundOption[]>([]);
  const [credits, setCredits] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  // --- Form State ---
  const [selectedFocus, setSelectedFocus] = useState<string[]>([]);
  const [notes, setNotes] = useState('');
  const [turnaroundId, setTurnaroundId] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const [estimatedDelivery, setEstimatedDelivery] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([fetchFocusAreas('speaking'), fetchTurnaroundOptions(), fetchBilling()])
      .then(([areas, options, billing]) => {
        setFocusAreas(areas);
        setTurnaroundOptions(options);
        if (options.length > 0) setTurnaroundId(options[0].id);
        setCredits(billing.reviewCredits);
      })
      .catch(() => setError('Failed to load review options. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  const toggleFocus = (areaId: string) => {
    setSelectedFocus(prev =>
      prev.includes(areaId) ? prev.filter(i => i !== areaId) : [...prev, areaId]
    );
  };

  const selectedTurnaround = turnaroundOptions.find(t => t.id === turnaroundId);
  const selectedCost = selectedTurnaround?.cost ?? 1;
  const hasEnoughCredits = credits >= selectedCost;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitError(null);

    if (!hasEnoughCredits) {
      setSubmitError('You need more review credits before this tutor review can be requested.');
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await submitReviewRequest({ submissionId: id, turnaroundId, focusAreas: selectedFocus, notes });
      analytics.track('review_requested', { submissionId: id, subtest: 'speaking', turnaroundId, focusCount: selectedFocus.length });
      setEstimatedDelivery(response.estimatedDelivery);
      setIsSuccess(true);
    } catch (err) {
      setSubmitError(isApiError(err) ? err.userMessage : err instanceof Error ? err.message : 'Failed to submit the tutor review request. Please try again.');
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isSuccess) {
    return (
      <LearnerPageHero
        eyebrow="Beyond AI Evaluation"
        icon={CheckCircle2}
        accent="emerald"
        title="Request Submitted"
        description={`Your recording has been queued for tutor review. ${selectedCost} review credit${selectedCost > 1 ? 's were' : ' was'} used${estimatedDelivery ? `, and the estimated turnaround is ${estimatedDelivery}` : ''}.`}
        aside={(
          <Button asChild>
            <Link href="/speaking">
              Back to Dashboard <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
            </Link>
          </Button>
        )}
      />
    );
  }

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="card-grid" />
      </>
    );
  }

  if (error) {
    return <ErrorState message={error} />;
  }

  return (
    <>
      {/* The "Beyond AI Evaluation" note is the page's purpose, so it is the hero, not a callout under it. */}
      <LearnerPageHero
        eyebrow="Beyond AI Evaluation"
        icon={Sparkles}
        accent="speaking"
        title="Request Tutor Review"
        description="While our AI provides immediate insights, a Tutor Review offers deep clinical nuance, specific OET grading, and personalized coaching from certified healthcare educators."
      />

      <form onSubmit={handleSubmit} className="space-y-6 sm:space-y-8">
        {/* Focus Areas */}
        <MotionSection>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader
              icon={Target}
              title="Focus Areas"
              description="Select specific criteria you want the reviewer to prioritize."
              className="mb-6"
            />

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {focusAreas.map((area) => {
                const isSelected = selectedFocus.includes(area.id);
                return (
                  <button
                    key={area.id}
                    type="button"
                    aria-pressed={isSelected}
                    onClick={() => toggleFocus(area.id)}
                    className={`flex items-start gap-4 rounded-control border-2 p-4 text-start transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                      isSelected ? 'border-primary bg-primary/5' : 'border-border bg-surface hover:border-border-hover'
                    }`}
                  >
                    <span
                      aria-hidden="true"
                      className={`mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 transition-colors duration-200 ${
                        isSelected ? 'border-primary bg-primary dark:border-violet-700 dark:bg-violet-700' : 'border-border-hover'
                      }`}
                    >
                      {isSelected && <CheckCircle2 className="h-4 w-4 text-white" />}
                    </span>
                    <span className="min-w-0">
                      <span className="block text-sm font-bold text-navy">{area.label}</span>
                      <span className="block text-xs leading-relaxed text-muted">{area.description}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          </Card>
        </MotionSection>

        {/* Reviewer Notes */}
        <MotionSection delayIndex={1}>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader icon={MessageSquare} title="Reviewer Notes" className="mb-6" />
            <textarea
              aria-label="Reviewer notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="E.g., 'I struggled with the transition to the physical exam explanation. Please check my empathy during the patient's interruption.'"
              className="h-32 w-full resize-none rounded-control border border-border bg-surface p-4 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
          </Card>
        </MotionSection>

        {/* Priority & Turnaround */}
        <MotionSection delayIndex={2}>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader icon={Clock} title="Priority & Turnaround" className="mb-6" />

            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              {turnaroundOptions.map((opt) => (
                <button
                  key={opt.id}
                  type="button"
                  aria-pressed={turnaroundId === opt.id}
                  onClick={() => setTurnaroundId(opt.id)}
                  className={`rounded-control border-2 p-5 text-center transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                    turnaroundId === opt.id ? 'border-primary bg-primary/5' : 'border-border bg-surface hover:border-border-hover'
                  }`}
                >
                  <span className="mb-1 block text-sm font-bold text-navy">{opt.label}</span>
                  <span className="mb-2 block text-xs font-bold text-primary">{opt.time}</span>
                  <span className="block eyebrow tabular-nums text-muted">{opt.cost} Credit{opt.cost > 1 ? 's' : ''}</span>
                </button>
              ))}
            </div>
          </Card>
        </MotionSection>

        {/* Review Credits */}
        <MotionSection delayIndex={3}>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader icon={CreditCard} title="Review Credits" className="mb-6" />

            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-primary/5 p-4">
              <div className="min-w-0">
                <p className="text-sm font-bold text-navy">Use Review Credits</p>
                <p className="text-xs tabular-nums text-muted">You have {credits} credit{credits !== 1 ? 's' : ''} remaining</p>
              </div>
              <p className="eyebrow tabular-nums text-primary">
                -{selectedCost} Credit{selectedCost > 1 ? 's' : ''}
              </p>
            </div>
            {!hasEnoughCredits ? (
              <InlineAlert variant="warning" className="mt-4">
                This tutor review needs {selectedCost} credit{selectedCost > 1 ? 's' : ''}. <Link href="/billing" className="font-bold underline">Top up review credits</Link> before submitting.
              </InlineAlert>
            ) : null}
          </Card>
        </MotionSection>

        {submitError ? <InlineAlert variant="error">{submitError}</InlineAlert> : null}

        {/* Submit Button */}
        <div className="space-y-3">
          <Button
            type="submit"
            fullWidth
            size="lg"
            loading={isSubmitting}
            disabled={isSubmitting || selectedFocus.length === 0 || !turnaroundId || !hasEnoughCredits}
          >
            {isSubmitting ? 'Submitting Request...' : (
              <>Submit Tutor Review Request <ArrowRight className="h-5 w-5 rtl:rotate-180" aria-hidden="true" /></>
            )}
          </Button>

          {selectedFocus.length === 0 && (
            <p className="text-center text-sm font-medium text-warning-strong">
              Please select at least one focus area
            </p>
          )}
        </div>
      </form>
    </>
  );
}

export default function ExpertReviewRequest() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <ExpertReviewRequestContent />
    </Suspense>
  );
}
