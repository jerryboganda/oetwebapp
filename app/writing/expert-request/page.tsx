'use client';

import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionPage } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchBilling, fetchFocusAreas, fetchTurnaroundOptions, submitReviewRequest } from '@/lib/api';
import type { FocusArea, TurnaroundOption } from '@/lib/mock-data';
import {
    CheckCircle2, ChevronLeft,
    Clock, CreditCard, Info, MessageSquare, ShieldCheck, Target, Zap
} from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useState } from 'react';

export default function WritingExpertReviewRequest() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const submissionId = searchParams?.get('id') ?? 'we-001';
  const [turnaroundOptions, setTurnaroundOptions] = useState<TurnaroundOption[]>([]);
  const [focusAreaOptions, setFocusAreaOptions] = useState<FocusArea[]>([]);
  const [availableCredits, setAvailableCredits] = useState(0);
  const [loading, setLoading] = useState(true);

  const [speed, setSpeed] = useState('');
  const [selectedFocus, setSelectedFocus] = useState<string[]>([]);
  const [notes, setNotes] = useState('');
  const [payment, setPayment] = useState('credit');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);

  useEffect(() => {
    analytics.track('content_view', { content: 'expert_request', subtest: 'writing' });
    Promise.all([fetchTurnaroundOptions(), fetchFocusAreas(), fetchBilling()])
      .then(([t, f, b]) => {
        setTurnaroundOptions(t);
        setFocusAreaOptions(f);
        setAvailableCredits(b.reviewCredits);
        if (t.length) setSpeed(t[0].id);
      })
      .finally(() => setLoading(false));
  }, []);

  const toggleFocus = (areaId: string) => {
    setSelectedFocus(prev =>
      prev.includes(areaId) ? prev.filter(a => a !== areaId) : prev.length < 3 ? [...prev, areaId] : prev,
    );
  };

  const selectedCost = turnaroundOptions.find(o => o.id === speed)?.cost ?? 1;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      await submitReviewRequest({ submissionId, turnaroundId: speed, focusAreas: selectedFocus, notes });
      analytics.track('review_requested', { turnaround: speed, focusCount: selectedFocus.length, subtest: 'writing' });
      setIsSuccess(true);
      setTimeout(() => router.push(`/writing/result?id=${submissionId}`), 3000);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Request Expert Review">
        <div className="space-y-6">
          <Skeleton className="h-24 rounded-2xl" />
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (isSuccess) {
    return (
      <LearnerDashboardShell pageTitle="Request Submitted">
        <div className="flex items-center justify-center min-h-[60vh] p-4">
          <MotionPage>
            <Card className="p-8 max-w-md w-full text-center">
              <div className="w-20 h-20 bg-success/10 text-success rounded-full flex items-center justify-center mx-auto mb-6"><CheckCircle2 className="w-10 h-10" /></div>
              <h1 className="text-2xl font-bold text-navy mb-2">Request Submitted!</h1>
              <p className="text-muted mb-8">Your submission has been sent to our expert panel. You will receive a notification once the review is complete.</p>
              <div className="text-sm text-muted/60 animate-pulse">Redirecting to results…</div>
            </Card>
          </MotionPage>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Request Expert Review">
      {/* Sticky header */}
      <header className="bg-surface border-b border-border sticky top-0 z-30 px-4 sm:px-6 py-4 flex items-center justify-between shadow-sm">
        <div className="flex items-center gap-4">
          <Link href={`/writing/result?id=${submissionId}`} className="text-muted hover:text-navy transition-colors p-2 -m-2 touch-target"><ChevronLeft className="w-5 h-5" /></Link>
          <h1 className="font-bold text-lg text-navy leading-tight">Request Expert Review</h1>
        </div>
      </header>

      <main className="py-8">
        {/* Context Banner */}
        <div className="bg-navy text-white rounded-2xl p-6 mb-8 shadow-md relative overflow-hidden">
          <div className="absolute top-0 right-0 w-32 h-32 bg-white/5 rounded-full -mr-16 -mt-16" />
          <div className="relative z-10">
            <div className="flex items-center gap-2 text-info text-xs font-bold uppercase tracking-widest mb-2"><ShieldCheck className="w-4 h-4" /> Human-in-the-loop</div>
            <h2 className="text-xl font-bold mb-1">Writing Submission</h2>
            <div className="text-sm text-info/70">{availableCredits} credits available</div>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="space-y-8">
          {/* 1. Turnaround Speed */}
          <section>
            <h3 className="text-sm font-bold text-muted uppercase tracking-wider mb-4 flex items-center gap-2"><Clock className="w-4 h-4" /> 1. Turnaround Speed</h3>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              {turnaroundOptions.map(option => (
                <label key={option.id} className={`relative flex flex-col p-4 rounded-xl border-2 cursor-pointer transition-all ${speed === option.id ? 'border-primary bg-primary/5 ring-4 ring-primary/5' : 'border-border bg-surface hover:border-border-hover'}`}>
                  <input type="radio" name="speed" className="sr-only" checked={speed === option.id} onChange={() => setSpeed(option.id)} />
                  <div className="flex items-center justify-between mb-1">
                    <span className="font-bold text-navy">{option.label}</span>
                    {option.id === 'express' && <Zap className="w-4 h-4 text-primary" />}
                  </div>
                  <div className="text-2xl font-black text-primary mb-1">{option.time}</div>
                  <div className="text-xs text-muted leading-snug">{option.description}</div>
                  <div className="mt-3 pt-3 border-t border-border text-xs font-bold text-navy">Cost: {option.cost} {option.cost === 1 ? 'Credit' : 'Credits'}</div>
                </label>
              ))}
            </div>
          </section>

          {/* 2. Focus Areas */}
          <section>
            <h3 className="text-sm font-bold text-muted uppercase tracking-wider mb-4 flex items-center gap-2"><Target className="w-4 h-4" /> 2. Focus Areas</h3>
            <p className="text-xs text-muted mb-4 italic">Select up to 3 areas you want the expert to prioritize.</p>
            <div className="flex flex-wrap gap-2">
              {focusAreaOptions.map(area => {
                const isSelected = selectedFocus.includes(area.id);
                const isDisabled = !isSelected && selectedFocus.length >= 3;
                return (
                  <button key={area.id} type="button" disabled={isDisabled} onClick={() => toggleFocus(area.id)}
                    className={`px-4 py-2 rounded-full text-sm font-medium border transition-all ${isSelected ? 'bg-primary border-primary text-white shadow-sm' : isDisabled ? 'bg-background-light border-border text-muted/40 cursor-not-allowed' : 'bg-surface border-border text-muted hover:border-border-hover'}`}>
                    {area.label}
                  </button>
                );
              })}
            </div>
          </section>

          {/* 3. Notes */}
          <section>
            <h3 className="text-sm font-bold text-muted uppercase tracking-wider mb-4 flex items-center gap-2"><MessageSquare className="w-4 h-4" /> 3. Notes for Reviewer</h3>
            <textarea value={notes} onChange={e => setNotes(e.target.value)} placeholder="e.g., I'm struggling with the discharge plan structure…" className="w-full h-32 p-4 rounded-xl border-2 border-border focus:border-primary focus:ring-4 focus:ring-primary/5 outline-none transition-all resize-none text-sm" />
          </section>

          {/* 4. Payment */}
          <section>
            <h3 className="text-sm font-bold text-muted uppercase tracking-wider mb-4 flex items-center gap-2"><CreditCard className="w-4 h-4" /> 4. Payment Method</h3>
            <div className="space-y-3">
              {[{ id: 'credit', label: 'Use Expert Credit', balance: `${availableCredits} Credits available`, value: `${selectedCost} Credit${selectedCost > 1 ? 's' : ''}` },
                { id: 'pay', label: 'Pay Per Review', balance: '$15.00', value: '$15.00' }].map(option => (
                <label key={option.id} className={`flex items-center justify-between p-4 rounded-xl border-2 cursor-pointer transition-all ${payment === option.id ? 'border-primary bg-primary/5' : 'border-border bg-surface hover:border-border-hover'}`}>
                  <div className="flex items-center gap-3">
                    <input type="radio" name="payment" className="w-4 h-4 text-primary focus:ring-primary" checked={payment === option.id} onChange={() => setPayment(option.id)} />
                    <div><div className="font-bold text-navy">{option.label}</div><div className="text-xs text-muted">{option.balance}</div></div>
                  </div>
                  <div className="font-black text-navy">{option.value}</div>
                </label>
              ))}
            </div>
          </section>

          {/* Disclaimer */}
          <div className="bg-background-light rounded-xl p-4 flex items-start gap-3">
            <Info className="w-5 h-5 text-muted/60 shrink-0 mt-0.5" />
            <p className="text-xs text-muted leading-relaxed">Expert reviews are conducted by certified OET trainers. Unlike AI evaluations, these provide nuanced human judgment and specific pedagogical advice. Turnaround times are guaranteed.</p>
          </div>

          {/* Submit */}
          <Button type="submit" fullWidth loading={isSubmitting} disabled={payment === 'credit' && availableCredits < selectedCost}>
            Submit Review Request ({selectedCost} Credit{selectedCost > 1 ? 's' : ''})
          </Button>
        </form>
      </main>
    </LearnerDashboardShell>
  );
}
