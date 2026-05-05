'use client';

import { AlertCircle, CheckCircle2, ChevronRight, FileText, Headphones, Loader2, Mic, Target, TrendingUp, UserCheck, Zap } from 'lucide-react';
import { motion, useReducedMotion } from 'motion/react';
import { useEffect, useState } from 'react';
import { getRecordingPulseTransition, prefersReducedMotion } from '@/lib/motion';
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
import { SpeakingSelfPracticeButton } from '@/components/domain/speaking-self-practice-button';
import { SpeakingScoreDisclaimer } from '@/components/domain/SpeakingScoreDisclaimer';
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

type SpeakingCriterion = NonNullable<SpeakingResult['criteria']>[number];

function criterionLabel(code: string) {
  return code
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (m) => m.toUpperCase())
    .trim();
}

export default function SpeakingResultSummary() {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const recordingPulseTransition = getRecordingPulseTransition(reducedMotion);
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
                    transition={{
                      ...recordingPulseTransition,
                      delay: reducedMotion ? 0 : index * 0.2,
                      repeatType: 'reverse',
                    }}
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

  const strongestCriterion = result.criteria?.reduce<SpeakingCriterion | undefined>((best, item) => (
    !best || item.score / Math.max(1, item.max) > best.score / Math.max(1, best.max) ? item : best
  ), undefined);
  const weakestCriterion = result.criteria?.reduce<SpeakingCriterion | undefined>((weakest, item) => (
    !weakest || item.score / Math.max(1, item.max) < weakest.score / Math.max(1, weakest.max) ? item : weakest
  ), undefined);

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

        {/* Wave 7: standardised "Estimated score, not official OET"
            disclaimer banner. Required on every speaking results page. */}
        <SpeakingScoreDisclaimer />

        <div className="grid grid-cols-1 gap-4 md:grid-cols-5">
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
          <Card className="p-4">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Strongest</p>
            <p className="mt-2 text-base font-bold text-navy">
              {strongestCriterion ? criterionLabel(strongestCriterion.criterionCode) : 'More evidence needed'}
            </p>
          </Card>
          <Card className="p-4">
            <p className="text-xs font-bold uppercase tracking-widest text-muted">Next focus</p>
            <p className="mt-2 text-base font-bold text-navy">
              {weakestCriterion ? criterionLabel(weakestCriterion.criterionCode) : 'Phrasing drill'}
            </p>
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
              <div className="flex items-center justify-center md:justify-start gap-2 mb-2 flex-wrap">
                <span className="text-xs font-bold text-muted uppercase tracking-widest">Estimated Score Range</span>
                <Badge variant={result.confidence === 'High' ? 'success' : result.confidence === 'Medium' ? 'warning' : 'danger'} size="sm">
                  {result.confidence} Band
                </Badge>
                {result.readinessBandLabel ? (
                  <Badge
                    variant={
                      result.readinessBand === 'strong' || result.readinessBand === 'exam_ready'
                        ? 'success'
                        : result.readinessBand === 'borderline'
                          ? 'warning'
                          : 'danger'
                    }
                    size="sm"
                  >
                    {result.readinessBandLabel}
                  </Badge>
                ) : null}
              </div>
              <div className="text-6xl font-black text-navy tracking-tighter mb-4">
                {result.scoreRange}
              </div>
              {typeof result.estimatedScaledScore === 'number' && typeof result.passThreshold === 'number' ? (
                <p className="text-xs text-muted mb-3">
                  Estimated <strong className="text-navy">{result.estimatedScaledScore}/500</strong> · pass threshold{' '}
                  <strong className="text-navy">{result.passThreshold}/500</strong>
                  {typeof result.rubricMax === 'number' ? <> · advisory rubric anchor 70/100 ≡ {result.passThreshold}/500</> : null}
                </p>
              ) : null}
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
              {/* Wave 5: deep-link this attempt's scenario into the
                  AI-patient Conversation module for unlimited
                  rehearsal. Falls back to no button when the result
                  has no source taskId. */}
              {result.taskId ? (
                <SpeakingSelfPracticeButton
                  taskId={result.taskId}
                  label="Practise with AI patient"
                />
              ) : null}
            </div>
          </Card>
        </MotionSection>

        {result.criteria && result.criteria.length > 0 ? (
          <MotionSection delayIndex={1}>
            <Card className="p-8">
              <div className="flex items-center justify-between gap-3 mb-6 flex-wrap">
                <div>
                  <h2 className="text-lg font-black text-navy">Criterion-by-criterion breakdown</h2>
                  <p className="text-xs text-muted mt-1">
                    OET Speaking has nine advisory criteria — four linguistic (each scored out of 6) and five clinical-communication (each scored out of 3).
                  </p>
                </div>
                {result.criteriaSource ? (
                  <Badge
                    variant={result.criteriaSource === 'ai_grounded' ? 'success' : 'warning'}
                    size="sm"
                  >
                    {result.criteriaSource === 'ai_grounded' ? 'AI grounded' : 'Rulebook fallback'}
                  </Badge>
                ) : null}
              </div>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                {(['linguistic', 'clinical'] as const).map((family) => {
                  const items = result.criteria!.filter((c) => c.family === family);
                  if (items.length === 0) return null;
                  return (
                    <div key={family} className="rounded-2xl border border-border p-4">
                      <p className="text-[11px] uppercase tracking-[0.18em] text-muted mb-3">
                        {family === 'linguistic' ? 'Linguistic (0–6)' : 'Clinical communication (0–3)'}
                      </p>
                      <ul className="space-y-3">
                        {items.map((criterion) => {
                          const pct = criterion.max > 0 ? Math.min(100, Math.max(0, (criterion.score / criterion.max) * 100)) : 0;
                          const label = criterionLabel(criterion.criterionCode);
                          return (
                            <li key={criterion.criterionCode}>
                              <div className="flex items-baseline justify-between gap-3">
                                <span className="text-sm font-semibold text-navy">{label}</span>
                                <span className="font-mono text-sm tabular-nums text-navy">
                                  {criterion.score}/{criterion.max}
                                </span>
                              </div>
                              <div className="mt-1 h-1.5 rounded-full bg-background-light overflow-hidden">
                                <div
                                  className="h-full bg-primary transition-all"
                                  style={{ width: `${pct}%` }}
                                  aria-hidden="true"
                                />
                              </div>
                              {criterion.linkedRuleIds && criterion.linkedRuleIds.length > 0 ? (
                                <p className="mt-1 text-[11px] text-muted">
                                  Linked rules: {criterion.linkedRuleIds.join(', ')}
                                </p>
                              ) : null}
                            </li>
                          );
                        })}
                      </ul>
                    </div>
                  );
                })}
              </div>
            </Card>
          </MotionSection>
        ) : null}

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
                <Link href="/recalls/words">
                  <Button variant="outline">
                    <Mic className="w-4 h-4" /> Open Recalls Audio
                  </Button>
                </Link>
                <Link href="/recalls/words">
                  <Button variant="ghost">
                    Practice recall words
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
