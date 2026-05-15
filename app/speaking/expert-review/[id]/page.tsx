'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams } from 'next/navigation';
import {
  Clock, CreditCard, MessageSquare, CheckCircle2,
  Target, Loader2, ArrowRight, Sparkles,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionPage } from '@/components/ui/motion-primitives';
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
      <LearnerDashboardShell pageTitle="Request Submitted">
        <div className="flex-1 flex flex-col items-center justify-center p-6 text-center">
          <MotionPage className="max-w-md w-full">
            <div className="w-24 h-24 bg-success/10 rounded-3xl flex items-center justify-center mx-auto mb-8">
              <CheckCircle2 className="w-12 h-12 text-success" />
            </div>
            <h1 className="text-3xl font-black text-navy mb-4 tracking-tight">Request Submitted</h1>
            <p className="text-muted mb-10 leading-relaxed">
              Your recording has been queued for tutor review. {selectedCost} review credit{selectedCost > 1 ? 's were' : ' was'} used, and the estimated turnaround is {estimatedDelivery ?? '48-72 hours'}.
            </p>
            <Link href="/speaking">
              <Button size="lg">
                Back to Dashboard <ArrowRight className="w-5 h-5" />
              </Button>
            </Link>
          </MotionPage>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Tutor Review Request">
        <div className="space-y-6">
          <Skeleton className="h-32 rounded-xl" />
          <Skeleton className="h-48 rounded-xl" />
          <Skeleton className="h-32 rounded-xl" />
          <Skeleton className="h-48 rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Tutor Review Request">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Tutor Review Request">
      <main className="flex-1">
        <form onSubmit={handleSubmit} className="max-w-3xl mx-auto space-y-8">

          {/* AI vs Human Distinction */}
          <InlineAlert variant="info">
            <div className="flex items-start gap-4">
                <Sparkles className="w-6 h-6 text-info shrink-0" />
              <div>
                <h2 className="text-sm font-bold text-info uppercase tracking-widest mb-1">Beyond AI Evaluation</h2>
                <p className="text-sm text-info/80 leading-relaxed">
                  While our AI provides immediate insights, a Tutor Review offers deep clinical nuance, specific OET grading, and personalized coaching from certified healthcare educators.
                </p>
              </div>
            </div>
          </InlineAlert>

          {/* Focus Areas */}
          <Card className="p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="w-10 h-10 rounded-xl bg-primary/5 flex items-center justify-center">
                <Target className="w-5 h-5 text-primary" />
              </div>
              <h2 className="text-lg font-black text-navy">Focus Areas</h2>
            </div>
            <p className="text-sm text-muted mb-6">Select specific criteria you want the reviewer to prioritize.</p>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {focusAreas.map((area) => (
                <button
                  key={area.id}
                  type="button"
                  onClick={() => toggleFocus(area.id)}
                  className={`flex items-start gap-4 p-4 rounded-2xl border-2 transition-all text-left ${
                    selectedFocus.includes(area.id) ? 'border-primary bg-primary/5' : 'border-border hover:border-border-hover bg-surface'
                  }`}
                >
                  <div className={`w-5 h-5 rounded-md border-2 mt-0.5 flex items-center justify-center transition-all ${
                    selectedFocus.includes(area.id) ? 'bg-primary border-primary' : 'border-border-hover'
                  }`}>
                    {selectedFocus.includes(area.id) && <CheckCircle2 className="w-4 h-4 text-white" />}
                  </div>
                  <div>
                    <h3 className="text-sm font-bold text-navy">{area.label}</h3>
                    <p className="text-xs text-muted leading-relaxed">{area.description}</p>
                  </div>
                </button>
              ))}
            </div>
          </Card>

          {/* Reviewer Notes */}
          <Card className="p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="w-10 h-10 rounded-xl bg-primary/5 flex items-center justify-center">
                <MessageSquare className="w-5 h-5 text-primary" />
              </div>
              <h2 className="text-lg font-black text-navy">Reviewer Notes</h2>
            </div>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="E.g., 'I struggled with the transition to the physical exam explanation. Please check my empathy during the patient's interruption.'"
              className="w-full h-32 p-4 bg-surface border border-border text-navy rounded-2xl text-sm focus:outline-none focus:ring-2 focus:ring-primary/20 resize-none"
            />
          </Card>

          {/* Priority & Turnaround */}
          <Card className="p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="w-10 h-10 rounded-xl bg-primary/5 flex items-center justify-center">
                <Clock className="w-5 h-5 text-primary" />
              </div>
              <h2 className="text-lg font-black text-navy">Priority & Turnaround</h2>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {turnaroundOptions.map((opt) => (
                <button
                  key={opt.id}
                  type="button"
                  onClick={() => setTurnaroundId(opt.id)}
                  className={`p-6 rounded-2xl border-2 transition-all text-center ${
                    turnaroundId === opt.id ? 'border-primary bg-primary/5' : 'border-border hover:border-border-hover bg-surface'
                  }`}
                >
                  <h3 className="text-sm font-bold text-navy mb-1">{opt.label}</h3>
                  <p className="text-xs text-primary font-bold mb-2">{opt.time}</p>
                  <p className="text-xs text-muted font-bold uppercase tracking-widest">{opt.cost} Credit{opt.cost > 1 ? 's' : ''}</p>
                </button>
              ))}
            </div>
          </Card>

          {/* Review Credits */}
          <Card className="p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="w-10 h-10 rounded-xl bg-primary/5 flex items-center justify-center">
                <CreditCard className="w-5 h-5 text-primary" />
              </div>
              <h2 className="text-lg font-black text-navy">Review Credits</h2>
            </div>

            <div className="flex items-center justify-between p-6 rounded-2xl border-2 border-primary bg-primary/5">
              <div>
                <h3 className="text-sm font-bold text-navy">Use Review Credits</h3>
                <p className="text-xs text-muted">You have {credits} credit{credits !== 1 ? 's' : ''} remaining</p>
              </div>
              <div className="text-xs font-bold text-primary uppercase tracking-widest">
                -{selectedCost} Credit{selectedCost > 1 ? 's' : ''}
              </div>
            </div>
            {!hasEnoughCredits ? (
              <div className="mt-4">
                <InlineAlert variant="warning">
                  This tutor review needs {selectedCost} credit{selectedCost > 1 ? 's' : ''}. <Link href="/billing" className="font-bold underline">Top up review credits</Link> before submitting.
                </InlineAlert>
              </div>
            ) : null}
          </Card>

          {submitError ? <InlineAlert variant="error">{submitError}</InlineAlert> : null}

          {/* Submit Button */}
          <Button
            type="submit"
            fullWidth
            size="lg"
            loading={isSubmitting}
            disabled={isSubmitting || selectedFocus.length === 0 || !turnaroundId || !hasEnoughCredits}
          >
            {isSubmitting ? 'Submitting Request...' : (
              <>Submit Tutor Review Request <ArrowRight className="w-5 h-5" /></>
            )}
          </Button>

          {selectedFocus.length === 0 && (
            <p className="text-center text-xs font-bold text-warning uppercase tracking-widest">
              Please select at least one focus area
            </p>
          )}
        </form>
      </main>
    </LearnerDashboardShell>
  );
}

export default function ExpertReviewRequest() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Tutor Review Request">
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </LearnerDashboardShell>
    }>
      <ExpertReviewRequestContent />
    </Suspense>
  );
}
