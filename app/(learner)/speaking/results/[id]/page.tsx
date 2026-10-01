'use client';

import { AlertCircle, BarChart3, CheckCircle2, ChevronRight, Download, FileText, Headphones, Loader2, Mic, Target, TrendingUp, UserCheck, Zap } from 'lucide-react';
import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { CountUp } from '@/components/ui/count-up';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ResultGauge } from '@/components/domain/results/gauge';
import { CriterionScoreRow } from '@/components/domain/results/criterion-score-row';
import { apiClient, downloadSpeakingEvaluationPdf, fetchPronunciationSpeakingLinked, fetchSpeakingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { trackSpeaking } from '@/lib/analytics/speaking-events';
import { SpeakingSelfPracticeButton } from '@/components/domain/speaking-self-practice-button';
import { SpeakingScoreDisclaimer } from '@/components/domain/SpeakingScoreDisclaimer';
import { retrySpeakingEvaluation } from '@/lib/api/speaking-results';
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

// Tiles size to their container, not the viewport (DESIGN.md §7).
const TILE_GRID = 'grid grid-cols-[repeat(auto-fit,minmax(min(100%,9rem),1fr))] gap-3';

export default function SpeakingResultSummary() {
  const params = useParams();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';
  const router = useRouter();
  const [analysing, setAnalysing] = useState(true);
  const [result, setResult] = useState<SpeakingResult | null>(null);
  const [failedResult, setFailedResult] = useState<SpeakingResult | null>(null);
  const [pronunciationInsight, setPronunciationInsight] = useState<PronunciationLinkedAssessment | null>(null);
  const [pdfState, setPdfState] = useState<'idle' | 'downloading' | 'error'>('idle');
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [error, setError] = useState(false);
  // 2026-05-27 audit fix — RULE_40 tone score from the Whisper transcript.
  const [tone, setTone] = useState<{
    toneScore: number;
    band: string;
    isAdvisory: boolean;
    provenance: string;
    rationale: string;
    rulebookRef: string;
  } | null>(null);

  // Bumping pollKey restarts polling ("Check again" / "Try grading again").
  const [pollKey, setPollKey] = useState(0);
  const [stillProcessing, setStillProcessing] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [retryError, setRetryError] = useState<string | null>(null);

  const restartPolling = () => {
    setFailedResult(null);
    setError(false);
    setStillProcessing(false);
    setAnalysing(true);
    setPollKey((key) => key + 1);
  };

  const retryGrading = async (attemptId: string) => {
    setRetrying(true);
    setRetryError(null);
    try {
      await retrySpeakingEvaluation(attemptId);
      restartPolling();
    } catch (err) {
      setRetryError(err instanceof Error ? err.message : 'Could not restart grading. Please try again.');
    } finally {
      setRetrying(false);
    }
  };

  useEffect(() => {
    let cancelled = false;
    let attempt = 0;
    let timer: ReturnType<typeof setTimeout> | null = null;

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
          // RULE_40 tone — fetch advisory score from the Whisper/AI tone pipeline.
          // Failure is non-fatal (BBN cards only have meaningful tone data).
          void apiClient.get(`/v1/speaking/sessions/${encodeURIComponent(id)}/tone`)
            .then((data) => { if (!cancelled && data) setTone(data); })
            .catch(() => { /* tone is best-effort */ });
          return;
        }

        // Terminal failure (e.g. AI grading credits exhausted, spec §9). Stop
        // polling and surface the reason instead of spinning on "Analyzing" forever.
        if (response.evalStatus === 'failed') {
          setFailedResult(response);
          setAnalysing(false);
          return;
        }

        // Back off from 2 s to 15 s and stop after ~10 min with a "Check
        // again" state — the result is saved server-side either way.
        attempt += 1;
        if (attempt >= 60) {
          setStillProcessing(true);
          setAnalysing(false);
          return;
        }
        timer = setTimeout(() => { void poll(); }, Math.min(15_000, Math.round(2000 * 1.2 ** attempt)));
      } catch {
        if (!cancelled) {
          setError(true);
          setAnalysing(false);
        }
      }
    };

    timer = setTimeout(() => { void poll(); }, 800);
    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
  }, [id, pollKey]);

  if (analysing) {
    // A spinner, not invented per-step progress bars: grading reports no steps.
    return (
      <Card padding="lg" className="flex flex-col items-center py-12 text-center" role="status">
        <div className="relative mb-6 flex h-16 w-16 items-center justify-center rounded-2xl bg-skill-speaking/10">
          <Loader2 className="h-8 w-8 animate-spin text-skill-speaking" aria-hidden="true" />
          <Mic className="absolute h-3.5 w-3.5 text-skill-speaking" aria-hidden="true" />
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-navy">Analyzing Recording</h1>
        <p className="mt-3 max-w-md text-sm leading-relaxed text-muted">
          Our AI is evaluating your performance against the speaking criteria for your selected exam family...
        </p>
      </Card>
    );
  }

  if (failedResult) {
    const noCredits = failedResult.statusReasonCode === 'ai_credits_insufficient';
    return (
      <div className="space-y-4">
        <InlineAlert variant={noCredits ? 'warning' : 'error'}>
          {failedResult.statusMessage
            ?? 'Grading could not be completed for this attempt. Please try again.'}
        </InlineAlert>
        {retryError ? <InlineAlert variant="error">{retryError}</InlineAlert> : null}
        <div className="flex flex-wrap gap-2">
          {failedResult.retryable && failedResult.attemptId ? (
            <Button onClick={() => void retryGrading(failedResult.attemptId as string)} disabled={retrying}>
              {retrying ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : null}
              Try grading again
            </Button>
          ) : null}
          {noCredits ? (
            <Button onClick={() => router.push('/ai-packages')}>Buy AI Credits</Button>
          ) : null}
          <Button variant="outline" onClick={() => router.push('/speaking')}>
            Back to Speaking
          </Button>
        </div>
      </div>
    );
  }

  if (stillProcessing || error || !result) {
    return (
      <InlineAlert
        variant={stillProcessing ? 'info' : 'error'}
        action={<Button size="sm" variant="outline" onClick={restartPolling}>Check again</Button>}
      >
        {stillProcessing
          ? 'Grading is taking longer than usual. Your recording is saved and the result will appear here.'
          : 'Could not load your speaking result. Please try again.'}
      </InlineAlert>
    );
  }

  const strongestCriterion = result.criteria?.reduce<SpeakingCriterion | undefined>((best, item) => (
    !best || item.score / Math.max(1, item.max) > best.score / Math.max(1, best.max) ? item : best
  ), undefined);
  const weakestCriterion = result.criteria?.reduce<SpeakingCriterion | undefined>((weakest, item) => (
    !weakest || item.score / Math.max(1, item.max) < weakest.score / Math.max(1, weakest.max) ? item : weakest
  ), undefined);

  const handleDownloadPdf = async () => {
    setPdfState('downloading');
    setPdfError(null);
    try {
      trackSpeaking('speaking_pdf_download_requested', { resultId: id });
      await downloadSpeakingEvaluationPdf(id);
      trackSpeaking('speaking_pdf_download_succeeded', { resultId: id });
      setPdfState('idle');
    } catch (err: unknown) {
      const message = err instanceof Error && err.message
        ? err.message
        : 'Could not download the PDF. Please try again later.';
      setPdfError(message);
      setPdfState('error');
      trackSpeaking('speaking_pdf_download_failed', {
        resultId: id,
        errorName: err instanceof Error && err.name ? err.name : 'unknown_error',
      });
    }
  };

  // Strongest / next focus only exist when the result has criteria: no stand-in labels.
  const heroHighlights = [
    { icon: FileText, label: 'Exam Family', value: result.examFamilyLabel },
    { icon: BarChart3, label: 'Confidence', value: result.confidenceLabel },
    ...(strongestCriterion ? [{ icon: Zap, label: 'Strongest', value: criterionLabel(strongestCriterion.criterionCode) }] : []),
    ...(weakestCriterion ? [{ icon: Target, label: 'Next focus', value: criterionLabel(weakestCriterion.criterionCode) }] : []),
  ];

  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking Results"
        icon={BarChart3}
        accent="speaking"
        title="Performance Summary"
        description="Review your estimated range, strongest signals, and the next action to keep your speaking momentum moving."
        highlights={heroHighlights}
      />

      <div className="space-y-3">
        <InlineAlert variant="info">
          <strong>{result.methodLabel}:</strong> {result.learnerDisclaimer}
        </InlineAlert>

        {/* Wave 7: standardised "Estimated score, not official OET"
            disclaimer banner. Required on every speaking results page. */}
        <SpeakingScoreDisclaimer />

        {result.humanReviewRecommended ? (
          <InlineAlert variant="warning">
            Human review is recommended before you rely on this estimate for high-stakes readiness decisions.
          </InlineAlert>
        ) : null}
      </div>

      <MotionSection>
        <Card padding="lg" className="flex flex-col items-center gap-8 md:flex-row">
          {typeof result.estimatedScaledScore === 'number' ? (
            <ResultGauge
              value={(result.estimatedScaledScore / 500) * 100}
              color={
                result.readinessBand === 'strong' || result.readinessBand === 'exam_ready'
                  ? 'var(--color-success)'
                  : result.readinessBand === 'borderline'
                    ? 'var(--color-warning)'
                    : result.readinessBand
                      ? 'var(--color-danger)'
                      : 'var(--color-primary)'
              }
            >
              <span className="text-2xl font-bold text-navy"><CountUp value={result.estimatedScaledScore} /></span>
              <span className="mt-0.5 tile-label text-muted">/ 500</span>
            </ResultGauge>
          ) : null}
          <div className="min-w-0 flex-1 text-center md:text-start">
            <div className="mb-2 flex flex-wrap items-center justify-center gap-2 md:justify-start">
              <span className="eyebrow text-muted">Estimated Score Range</span>
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
            <p className="mb-4 text-5xl font-black tabular-nums tracking-tighter text-navy sm:text-6xl">
              {result.scoreRange}
            </p>
            {typeof result.estimatedScaledScore === 'number' && typeof result.passThreshold === 'number' ? (
              <p className="text-xs tabular-nums text-muted">
                Estimated <strong className="text-navy">{result.estimatedScaledScore}/500</strong> · pass threshold{' '}
                <strong className="text-navy">{result.passThreshold}/500</strong>
                {typeof result.rubricMax === 'number' ? <> · advisory rubric anchor 70/100 ≡ {result.passThreshold}/500</> : null}
              </p>
            ) : null}
          </div>

          <div className="hidden h-24 w-px bg-border md:block" aria-hidden="true" />

          <div className="flex w-full flex-col gap-3 md:w-auto">
            <Button fullWidth asChild>
              <Link href={`/speaking/transcript/${id}`}>
                <FileText className="h-5 w-5" aria-hidden="true" /> Review Transcript
              </Link>
            </Button>
            <Button variant="outline" fullWidth asChild>
              <Link href={`/speaking/expert-review/${id}`}>
                <UserCheck className="h-5 w-5" aria-hidden="true" /> Request Tutor Review
              </Link>
            </Button>
            <Button
              variant="outline"
              fullWidth
              onClick={() => void handleDownloadPdf()}
              loading={pdfState === 'downloading'}
              disabled={pdfState === 'downloading'}
            >
              <Download className="h-5 w-5" aria-hidden="true" /> Download Practice PDF
            </Button>
            {pdfState === 'error' && pdfError ? (
              <p className="text-xs text-danger-strong" role="alert">{pdfError}</p>
            ) : null}
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
          <Card padding="lg">
            <LearnerSurfaceSectionHeader
              title="Criterion-by-criterion breakdown"
              description="OET Speaking has nine advisory criteria: four linguistic (each scored out of 6) and five clinical-communication (each scored out of 3)."
              action={result.criteriaSource ? (
                <div className="shrink-0">
                  <Badge
                    variant={result.criteriaSource === 'ai_grounded' ? 'success' : 'warning'}
                    size="sm"
                  >
                    {result.criteriaSource === 'ai_grounded' ? 'AI grounded' : 'Rulebook fallback'}
                  </Badge>
                </div>
              ) : undefined}
            />
            {/* Each row is already a tinted block, so the family groups stay plain. */}
            <div className="mt-6 grid grid-cols-1 gap-6 md:grid-cols-2">
              {(['linguistic', 'clinical'] as const).map((family) => {
                const items = result.criteria!.filter((c) => c.family === family);
                if (items.length === 0) return null;
                return (
                  <div key={family} className="min-w-0">
                    <p className="eyebrow mb-3 text-muted">
                      {family === 'linguistic' ? 'Linguistic (0–6)' : 'Clinical communication (0–3)'}
                    </p>
                    <ul className="space-y-3">
                      {items.map((criterion) => (
                        <li key={criterion.criterionCode}>
                          <CriterionScoreRow
                            label={criterionLabel(criterion.criterionCode)}
                            score={criterion.score}
                            max={criterion.max}
                            feedback={criterion.explanation ?? criterion.descriptor ?? null}
                            meta={criterion.linkedRuleIds && criterion.linkedRuleIds.length > 0
                              ? `Linked rules: ${criterion.linkedRuleIds.join(', ')}`
                              : null}
                          />
                        </li>
                      ))}
                    </ul>
                  </div>
                );
              })}
            </div>
          </Card>
        </MotionSection>
      ) : null}

      {/* 2026-05-27 audit fix — RULE_40 tone score card. Advisory only;
          human OET Assessor remains authoritative per RULE_57 / RULE_58. */}
      {tone ? (
        <MotionSection delayIndex={2}>
          <Card padding="lg" data-testid="speaking-tone-card">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 className="text-base font-bold text-navy">Tone of voice (RULE_40)</h2>
                <p className="mt-1 text-xs text-muted">{tone.provenance}</p>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant={tone.toneScore >= 3 ? 'success' : tone.toneScore >= 2 ? 'info' : tone.toneScore >= 1 ? 'warning' : 'danger'} size="sm">
                  {tone.band} · {tone.toneScore}/3
                </Badge>
                {tone.isAdvisory ? <Badge variant="info" size="sm">Advisory</Badge> : null}
              </div>
            </div>
            <p className="text-sm leading-relaxed text-navy">{tone.rationale}</p>
          </Card>
        </MotionSection>
      ) : null}

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        <MotionItem delayIndex={0} className="h-full">
          <Card padding="lg" className="h-full">
            <LearnerSurfaceSectionHeader
              icon={<Zap className="text-success-strong" aria-hidden="true" />}
              title="Key Strengths"
              className="mb-6"
            />
            <ul className="space-y-4">
              {result.strengths.map((strength, index) => (
                <li key={index} className="flex items-start gap-4">
                  <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-success-strong" aria-hidden="true" />
                  <p className="text-sm font-medium leading-relaxed text-navy">{strength}</p>
                </li>
              ))}
            </ul>
          </Card>
        </MotionItem>

        <MotionItem delayIndex={1} className="h-full">
          <Card padding="lg" className="h-full">
            <LearnerSurfaceSectionHeader
              icon={<Target className="text-warning-strong" aria-hidden="true" />}
              title="Top Improvements"
              className="mb-6"
            />
            <ul className="space-y-4">
              {result.improvements.map((improvement, index) => (
                <li key={index} className="flex items-start gap-4">
                  <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden="true" />
                  <p className="text-sm font-medium leading-relaxed text-navy">{improvement}</p>
                </li>
              ))}
            </ul>
          </Card>
        </MotionItem>
      </div>

      {pronunciationInsight ? (
        <MotionSection delayIndex={3}>
          <Card padding="lg">
            <LearnerSurfaceSectionHeader
              icon={<Headphones aria-hidden="true" />}
              title="Pronunciation Insight"
              className="mb-6"
            />
            <div className={`${TILE_GRID} mb-4 text-center`}>
              {[
                ['Accuracy', pronunciationInsight.accuracy],
                ['Fluency', pronunciationInsight.fluency],
                ['Complete', pronunciationInsight.completeness],
                ['Prosody', pronunciationInsight.prosody],
                ['Overall', pronunciationInsight.overall],
              ].map(([label, value]) => (
                <div key={label as string} className="rounded-xl bg-background-light p-3">
                  <div className="tile-label text-muted">{label}</div>
                  <div className="mt-1 text-xl font-bold text-navy"><CountUp value={value as number} /></div>
                </div>
              ))}
            </div>
            <p className="text-sm leading-relaxed text-muted">
              Advisory pronunciation projection from your latest linked speaking review:{' '}
              <strong className="tabular-nums text-navy">{pronunciationInsight.projectedSpeakingScaled}/500 · Grade {pronunciationInsight.projectedSpeakingGrade}</strong>.
              Use the targeted drill workflow to improve weak sounds, word stress, and intonation.
            </p>
            <Button variant="outline" asChild className="mt-4">
              <Link href="/recalls/words">
                <Mic className="h-4 w-4" aria-hidden="true" /> Open Recalls Audio
              </Link>
            </Button>
          </Card>
        </MotionSection>
      ) : null}

      {result.nextDrill && (
        <MotionSection delayIndex={4}>
          <Card padding="lg" className="flex flex-col gap-6 md:flex-row md:items-center md:justify-between">
            <div className="flex items-start gap-4">
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-skill-speaking/10 text-skill-speaking">
                <TrendingUp className="h-5 w-5" aria-hidden="true" />
              </span>
              <div className="min-w-0">
                <p className="eyebrow mb-1 text-muted">Recommended Next Drill</p>
                <h2 className="text-lg font-bold text-navy">{result.nextDrill.title}</h2>
                <p className="mt-1 max-w-xl text-sm text-muted">{result.nextDrill.description}</p>
              </div>
            </div>
            <Button size="lg" className="shrink-0" asChild>
              <Link href={result.nextDrill.route ?? `/speaking/phrasing/${result.nextDrill.id}`}>
                Start Drill <ChevronRight className="h-5 w-5 rtl:rotate-180" aria-hidden="true" />
              </Link>
            </Button>
          </Card>
        </MotionSection>
      )}
    </>
  );
}
