'use client';

import { Suspense, useState, useEffect } from 'react';
import {
  ChevronLeft,
  Clock,
  Target,
  MessageSquare,
  CreditCard,
  ShieldCheck,
  Zap,
  CheckCircle2,
} from 'lucide-react';
import { MotionSection } from '@/components/ui/motion-primitives';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { Button } from '@/components/ui/button';
import { PageSkeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchTurnaroundOptions, fetchFocusAreas, fetchBilling, isApiError, submitReviewRequest } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { TurnaroundOption, FocusArea } from '@/lib/mock-data';

function WritingExpertReviewContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const submissionId = searchParams?.get('id');
  const [turnaroundOptions, setTurnaroundOptions] = useState<TurnaroundOption[]>([]);
  const [focusAreaOptions, setFocusAreaOptions] = useState<FocusArea[]>([]);
  const [availableCredits, setAvailableCredits] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [speed, setSpeed] = useState('');
  const [selectedFocus, setSelectedFocus] = useState<string[]>([]);
  const [notes, setNotes] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const [estimatedDelivery, setEstimatedDelivery] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { content: 'expert_request', subtest: 'writing' });
    Promise.all([fetchTurnaroundOptions(), fetchFocusAreas('writing'), fetchBilling()])
      .then(([t, f, b]) => {
        setTurnaroundOptions(t);
        setFocusAreaOptions(f);
        setAvailableCredits(b.reviewCredits);
        if (t.length) setSpeed(t[0].id);
      })
      .catch(() => setError('Failed to load review request options. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  const toggleFocus = (areaId: string) => {
    setSelectedFocus(prev =>
      prev.includes(areaId) ? prev.filter(a => a !== areaId) : prev.length < 3 ? [...prev, areaId] : prev,
    );
  };

  const selectedCost = turnaroundOptions.find(o => o.id === speed)?.cost ?? 1;
  const hasEnoughCredits = availableCredits >= selectedCost;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!submissionId) {
      setError('Open tutor review requests from a completed writing result so we can attach the correct submission.');
      return;
    }

    if (!hasEnoughCredits) {
      setError('You need more review credits before this tutor review can be requested.');
      return;
    }

    setIsSubmitting(true);
    try {
      const response = await submitReviewRequest({ submissionId, turnaroundId: speed, focusAreas: selectedFocus, notes });
      analytics.track('review_requested', { turnaround: speed, focusCount: selectedFocus.length, subtest: 'writing' });
      setEstimatedDelivery(response.estimatedDelivery);
      setIsSuccess(true);
      setTimeout(() => router.push(`/writing/result?id=${submissionId}`), 3000);
    } catch (err) {
      setError(isApiError(err) ? err.userMessage : err instanceof Error ? err.message : 'Failed to submit the tutor review request. Please try again.');
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="card-grid" />
      </>
    );
  }

  if (isSuccess) {
    return (
      <LearnerPageHero
        eyebrow="Human-in-the-loop"
        icon={CheckCircle2}
        accent="emerald"
        title="Request Submitted!"
        description={`Your submission has been queued for tutor review. ${selectedCost} review credit${selectedCost > 1 ? 's were' : ' was'} used${estimatedDelivery ? `, and the estimated turnaround is ${estimatedDelivery}` : ''}.`}
        footer={<p role="status" className="text-sm text-muted">Redirecting to results…</p>}
      />
    );
  }

  const sectionHeading = 'mb-4 flex items-center gap-2 text-lg font-bold text-navy';

  return (
    <>
      {/* One page header: the old sticky bar and the navy "Writing Submission"
          banner were two headers for the same page; the credit balance shows
          once, in step 4. */}
      <LearnerPageHero
        eyebrow="Human-in-the-loop"
        icon={ShieldCheck}
        title="Request Tutor Review"
        description="Tutor reviews are conducted by certified OET trainers. Unlike AI evaluations, these provide nuanced human judgment and specific pedagogical advice. Turnaround times are guaranteed."
        aside={
          <Button asChild variant="outline" size="sm">
            <Link href={submissionId ? `/writing/result?id=${submissionId}` : '/writing'}>
              <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back
            </Link>
          </Button>
        }
      />

      {!submissionId ? (
        <InlineAlert variant="warning">
          Open tutor review requests from a completed writing result or your submissions history so the correct attempt is attached. <Link href="/submissions" className="font-bold underline">Go to submissions</Link>
        </InlineAlert>
      ) : null}

      <form onSubmit={handleSubmit} className="space-y-6 sm:space-y-8">
        {/* 1. Turnaround Speed */}
        <MotionSection delayIndex={0}>
          <h2 className={sectionHeading}><Clock className="h-4 w-4 text-primary" aria-hidden="true" /> 1. Turnaround Speed</h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {turnaroundOptions.map(option => (
              <label key={option.id} className={`relative flex min-w-0 cursor-pointer flex-col rounded-xl border-2 p-4 transition-[border-color,box-shadow,background-color] duration-200 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-primary ${speed === option.id ? 'border-primary bg-primary/5 ring-4 ring-primary/5' : 'border-border bg-surface hover:border-border-hover'}`}>
                <input type="radio" name="speed" className="sr-only" checked={speed === option.id} onChange={() => setSpeed(option.id)} />
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="font-bold text-navy">{option.label}</span>
                  {option.id === 'express' && <Zap className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />}
                </div>
                <div className="mb-1 text-2xl font-bold tabular-nums text-primary">{option.time}</div>
                <div className="text-xs leading-snug text-muted">{option.description}</div>
                <div className="mt-3 border-t border-border pt-3 text-xs font-bold tabular-nums text-navy">Cost: {option.cost} {option.cost === 1 ? 'Credit' : 'Credits'}</div>
              </label>
            ))}
          </div>
        </MotionSection>

        {/* 2. Focus Areas */}
        <MotionSection delayIndex={1}>
          <h2 className={sectionHeading}><Target className="h-4 w-4 text-primary" aria-hidden="true" /> 2. Focus Areas</h2>
          <p className="mb-4 text-xs italic text-muted">Select up to 3 areas you want the tutor to prioritize.</p>
          <div className="flex flex-wrap gap-2">
            {focusAreaOptions.map(area => {
              const isSelected = selectedFocus.includes(area.id);
              const isDisabled = !isSelected && selectedFocus.length >= 3;
              return (
                <button key={area.id} type="button" disabled={isDisabled} aria-pressed={isSelected} onClick={() => toggleFocus(area.id)}
                  className={`pressable min-h-11 rounded-full border px-4 py-2 text-sm font-medium transition-[color,background-color,border-color,box-shadow] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ${isSelected ? 'border-primary bg-primary text-white shadow-sm dark:bg-primary-700' : isDisabled ? 'cursor-not-allowed border-border bg-background-light text-muted/40' : 'hover-primary border-border bg-surface text-muted hover:border-border-hover'}`}>
                  {area.label}
                </button>
              );
            })}
          </div>
        </MotionSection>

        {/* 3. Notes */}
        <MotionSection delayIndex={2}>
          <h2 className={sectionHeading}><MessageSquare className="h-4 w-4 text-primary" aria-hidden="true" /> 3. Notes for Reviewer</h2>
          <textarea aria-label="Notes for reviewer" value={notes} onChange={e => setNotes(e.target.value)} placeholder="e.g., I'm struggling with the discharge plan structure…" className="h-32 w-full resize-none rounded-control border border-border bg-surface p-4 text-sm text-navy outline-none transition-[border-color,box-shadow] duration-200 placeholder:text-muted focus:border-primary focus-visible:ring-2 focus-visible:ring-primary" />
        </MotionSection>

        {/* 4. Credits */}
        <MotionSection delayIndex={3}>
          <h2 className={sectionHeading}><CreditCard className="h-4 w-4 text-primary" aria-hidden="true" /> 4. Review Credits</h2>
          <div className={`flex flex-wrap items-center justify-between gap-3 rounded-xl border-2 p-4 ${hasEnoughCredits ? 'border-primary bg-primary/5' : 'border-warning/40 bg-warning/10'}`}>
            <div className="min-w-0">
              <div className="font-bold text-navy">Use Review Credits</div>
              <div className="text-xs tabular-nums text-muted">{availableCredits} credits available</div>
            </div>
            <div className="font-bold tabular-nums text-navy">{selectedCost} Credit{selectedCost > 1 ? 's' : ''}</div>
          </div>
          {!hasEnoughCredits ? (
            <InlineAlert variant="warning" className="mt-3">
              This tutor review needs {selectedCost} credit{selectedCost > 1 ? 's' : ''}. <Link href="/billing" className="font-bold underline">Top up review credits</Link> before submitting.
            </InlineAlert>
          ) : null}
        </MotionSection>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* Submit */}
        <Button type="submit" fullWidth loading={isSubmitting} disabled={isSubmitting || !submissionId || !speed || !hasEnoughCredits}>
          Submit Tutor Review Request ({selectedCost} Credit{selectedCost > 1 ? 's' : ''})
        </Button>
      </form>
    </>
  );
}

export default function WritingExpertReviewRequest() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <WritingExpertReviewContent />
    </Suspense>
  );
}
