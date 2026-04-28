'use client';

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import {
  CheckCircle2, AlertCircle, TrendingUp, Zap, Target, Loader2, FileText, Mic, UserCheck, ChevronRight, Headphones,
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import { fetchPronunciationSpeakingLinked, fetchSpeakingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { SpeakingResult } from '@/lib/mock-data';

type PronunciationLinkedAssessment = {
  id: string;
  attemptId: string | null;
  accuracy: number;
  fluency: number;
  completeness: number;
  prosody: number;
  overall: number;
  projectedSpeakingScaled: number;
  projectedSpeakingGrade: string;
  createdAt: string;
};

export default function SpeakingResultSummary() {
  const params = useParams();
  const id = params?.id as string;
  const [analysing, setAnalysing] = useState(true);
  const [result, setResult] = useState<SpeakingResult | null>(null);
  const [pronunciationInsight, setPronunciationInsight] = useState<PronunciationLinkedAssessment | null>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const poll = async () => {
      try {
        const response = await fetchSpeakingResult(id);
        if (cancelled) return;
        if (response.evalStatus === 'completed') {
          setResult(response);
          void fetchPronunciationSpeakingLinked(10)
            .then((items) => {
              if (cancelled) return;
              const linked = (items as PronunciationLinkedAssessment[]).find((item) => item.attemptId === id)
                ?? (items as PronunciationLinkedAssessment[])[0]
                ?? null;
              setPronunciationInsight(linked);
            })
            .catch(() => {
              if (!cancelled) setPronunciationInsight(null);
            });
          analytics.track('evaluation_viewed', { resultId: id, subtest: 'speaking' });
          setAnalysing(false);
          return;
        }

        setTimeout(() => { void poll(); }, 2000);
      } catch {
        if (!cancelled) {
          setError(true);
          setAnalysing(false);
        }
      }
    };

    const timer = setTimeout(() => { void poll(); }, 800);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [id]);

  if (analysing) {
    return (
      <LearnerDashboardShell pageTitle="Analyzing...">
        <div className="flex-1 flex flex-col items-center justify-center p-6 text-center">
          <motion.div
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            className="max-w-md w-full bg-surface rounded-3xl p-10 shadow-xl border border-border"
          >
            <div className="w-20 h-20 bg-primary/5 rounded-3xl flex items-center justify-center mx-auto mb-8 relative">
              <Loader2 className="w-10 h-10 text-primary animate-spin" />
              <div className="absolute inset-0 flex items-center justify-center">
                <Mic className="w-4 h-4 text-primary/40" />
              </div>
            </div>
            <h1 className="text-3xl font-black text-navy mb-4 tracking-tight">Analyzing Recording</h1>
            <p className="text-muted mb-10 leading-relaxed">
              Our AI is evaluating your performance against the speaking criteria for your selected exam family...
            </p>
            <div className="space-y-4">
              {['Response quality', 'Task communication', 'Score estimation'].map((label, index) => (
                <div key={label} className="flex items-center justify-between p-4 bg-background-light rounded-2xl border border-border">
                  <span className="text-sm font-bold text-muted">{label}</span>
                  <motion.div
                    initial={{ width: 0 }}
                    animate={{ width: '40%' }}
                    transition={{ duration: 1, delay: index * 0.2, repeat: Infinity, repeatType: 'reverse' }}
                    className="h-1.5 bg-primary/20 rounded-full overflow-hidden"
                  >
                    <div className="h-full bg-primary w-full" />
                  </motion.div>
                </div>
              ))}
            </div>
          </motion.div>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error || !result) {
    return (
      <LearnerDashboardShell pageTitle="Results">
        <InlineAlert variant="error">Could not load your speaking result. Please try again later.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Performance Summary">
      <div className="space-y-6">
        <section className="space-y-2">
          <p className="text-xs font-bold uppercase tracking-[0.18em] text-muted">Speaking Results</p>
          <h1 className="text-3xl font-black tracking-tight text-navy">Performance Summary</h1>
          <p className="max-w-2xl text-sm text-muted">
            Review your estimated range, strongest signals, and the next action to keep your speaking momentum moving.
          </p>
        </section>

        <InlineAlert variant="info">
          <strong>{result.methodLabel}:</strong> {result.learnerDisclaimer}
        </InlineAlert>

        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <Card className="p-4">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Exam Family</p>
            <p className="mt-2 text-base font-bold text-navy">{result.examFamilyLabel}</p>
          </Card>
          <Card className="p-4">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Confidence</p>
            <p className="mt-2 text-base font-bold text-navy">{result.confidenceLabel}</p>
          </Card>
          <Card className="p-4">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Provenance</p>
            <p className="mt-2 text-base font-bold text-navy">{result.provenanceLabel}</p>
          </Card>
        </div>

        {result.humanReviewRecommended ? (
          <InlineAlert variant="warning">
            Human review is recommended before you rely on this estimate for high-stakes readiness decisions.
          </InlineAlert>
        ) : null}

        <MotionSection>
          <Card className="p-8 flex flex-col md:flex-row items-center gap-8">
            <div className="flex-1 text-center md:text-left">
              <div className="flex items-center justify-center md:justify-start gap-2 mb-2">
                <span className="text-xs font-bold text-muted uppercase tracking-widest">Estimated Score Range</span>
                <Badge variant={result.confidence === 'High' ? 'success' : result.confidence === 'Medium' ? 'warning' : 'danger'} size="sm">
                  {result.confidence} Band
                </Badge>
              </div>
              <div className="text-6xl font-black text-navy tracking-tighter mb-4">
                {result.scoreRange}
              </div>
              <p className="text-sm text-muted leading-relaxed max-w-md">
                {result.learnerDisclaimer}
              </p>
            </div>

            <div className="w-px h-24 bg-border hidden md:block" />

            <div className="flex flex-col gap-4 w-full md:w-auto">
              <Link href={`/speaking/transcript/${id}`}>
                <Button fullWidth>
                  <FileText className="w-5 h-5" /> Review Transcript
                </Button>
              </Link>
              <Link href={`/speaking/expert-review/${id}`}>
                <Button variant="outline" fullWidth>
                  <UserCheck className="w-5 h-5" /> Request Tutor Review
                </Button>
              </Link>
            </div>
          </Card>
        </MotionSection>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <MotionSection delayIndex={1}>
            <Card className="p-8 h-full">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-10 h-10 rounded-xl bg-success/10 flex items-center justify-center">
                  <Zap className="w-5 h-5 text-success" />
                </div>
                <h2 className="text-lg font-black text-navy">Key Strengths</h2>
              </div>
              <ul className="space-y-4">
                {result.strengths.map((strength, index) => (
                  <li key={index} className="flex gap-4 items-start">
                    <CheckCircle2 className="w-5 h-5 text-success shrink-0 mt-0.5" />
                    <p className="text-sm text-navy font-medium leading-relaxed">{strength}</p>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <Card className="p-8 h-full">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-10 h-10 rounded-xl bg-warning/10 flex items-center justify-center">
                  <Target className="w-5 h-5 text-warning" />
                </div>
                <h2 className="text-lg font-black text-navy">Top Improvements</h2>
              </div>
              <ul className="space-y-4">
                {result.improvements.map((improvement, index) => (
                  <li key={index} className="flex gap-4 items-start">
                    <AlertCircle className="w-5 h-5 text-warning shrink-0 mt-0.5" />
                    <p className="text-sm text-navy font-medium leading-relaxed">{improvement}</p>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>
        </div>

        {pronunciationInsight ? (
          <MotionSection delayIndex={3}>
            <Card className="p-8 h-full">
              <div className="flex items-center gap-3 mb-6">
                <div className="w-10 h-10 rounded-xl bg-danger/10 flex items-center justify-center">
                  <Headphones className="w-5 h-5 text-danger" />
                </div>
                <h2 className="text-lg font-black text-navy">Pronunciation Insight</h2>
              </div>
              <div className="grid grid-cols-2 md:grid-cols-5 gap-3 mb-4 text-center">
                {[
                  ['Accuracy', pronunciationInsight.accuracy],
                  ['Fluency', pronunciationInsight.fluency],
                  ['Complete', pronunciationInsight.completeness],
                  ['Prosody', pronunciationInsight.prosody],
                  ['Overall', pronunciationInsight.overall],
                ].map(([label, value]) => (
                  <div key={label as string} className="rounded-2xl border border-border p-3">
                    <div className="text-[11px] uppercase tracking-[0.15em] text-muted">{label}</div>
                    <div className="mt-1 font-mono text-xl font-semibold text-navy">{Math.round(value as number)}</div>
                  </div>
                ))}
              </div>
              <p className="text-sm text-muted leading-relaxed">
                Advisory pronunciation projection from your latest linked speaking review:{' '}
                <strong className="text-navy">{pronunciationInsight.projectedSpeakingScaled}/500 · Grade {pronunciationInsight.projectedSpeakingGrade}</strong>.
                Use the targeted drill workflow to improve weak sounds, word stress, and intonation.
              </p>
              <div className="mt-4 flex flex-wrap gap-3">
                <Link href="/pronunciation">
                  <Button variant="outline">
                    <Mic className="w-4 h-4" /> Open Pronunciation Drills
                  </Button>
                </Link>
                <Link href={`/pronunciation?focus=phoneme`}>
                  <Button variant="ghost">
                    Practice weak phonemes
                  </Button>
                </Link>
              </div>
            </Card>
          </MotionSection>
        ) : null}

        {result.nextDrill && (
          <MotionSection
            delayIndex={4}
            className="bg-navy rounded-3xl p-8 text-white flex flex-col md:flex-row items-center justify-between gap-8"
          >
            <div className="flex items-center gap-6">
              <div className="w-16 h-16 rounded-2xl bg-white/10 flex items-center justify-center shrink-0">
                <TrendingUp className="w-8 h-8 text-primary" />
              </div>
              <div>
                <div className="text-xs font-bold text-primary uppercase tracking-widest mb-1">Recommended Next Drill</div>
                <h3 className="text-xl font-black mb-2">{result.nextDrill.title}</h3>
                <p className="text-sm text-white/60 max-w-sm">{result.nextDrill.description}</p>
              </div>
            </div>
            <Link href={result.nextDrill.route ?? `/speaking/phrasing/${result.nextDrill.id}`}>
              <Button className="bg-primary text-white px-8 py-4 rounded-2xl font-black whitespace-nowrap">
                Start Drill <ChevronRight className="w-5 h-5" />
              </Button>
            </Link>
          </MotionSection>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
