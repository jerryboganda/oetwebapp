'use client';

import { useCallback, useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Mic, ArrowLeft, Volume2, Play, AlertTriangle } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchPronunciationDrill,
  fetchMyPronunciationProgress,
  pronunciationInitAttempt,
  pronunciationUploadAudio,
  fetchPronunciationEntitlement,
  type PronunciationEntitlement,
  type PronunciationProgressItem,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import {
  PronunciationRecorderPanel,
  PronunciationResultsCard,
} from '@/components/domain/pronunciation';

type PronunciationDrill = {
  id: string;
  targetPhoneme: string;
  label: string;
  profession: string;
  focus: string;
  primaryRuleId: string | null;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  audioModelUrl: string | null;
  audioModelAssetId: string | null;
  tipsHtml: string;
  difficulty: string;
};

type MinimalPair = { a: string; b: string };

type AssessmentResponse = {
  id: string;
  drillId: string | null;
  accuracy: number;
  fluency: number;
  completeness: number;
  prosody: number;
  overall: number;
  projectedSpeakingScaled: number;
  projectedSpeakingGrade: string;
  wordScoresJson: string;
  problematicPhonemesJson: string;
  fluencyMarkersJson: string;
  feedbackJson: string;
  provider: string;
};

function parseStringArray(value: string | null | undefined): string[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
  } catch { return []; }
}

function parseMinimalPairs(value: string | null | undefined): MinimalPair[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((pair): pair is MinimalPair => {
      if (!pair || typeof pair !== 'object') return false;
      const item = pair as Record<string, unknown>;
      return typeof item.a === 'string' && typeof item.b === 'string';
    });
  } catch { return []; }
}

export default function PronunciationDrillPage() {
  const params = useParams<{ drillId: string }>();
  const drillId = params?.drillId ?? '';

  const [drill, setDrill] = useState<PronunciationDrill | null>(null);
  const [progressMap, setProgressMap] = useState<Record<string, PronunciationProgressItem>>({});
  const [entitlement, setEntitlement] = useState<PronunciationEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [assessment, setAssessment] = useState<AssessmentResponse | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);

  useEffect(() => {
    if (!drillId) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      const [drillResult, progressResult, entitlementResult] = await Promise.allSettled([
        fetchPronunciationDrill(drillId),
        fetchMyPronunciationProgress(),
        fetchPronunciationEntitlement(),
      ]);
      if (cancelled) return;
      if (drillResult.status === 'fulfilled') {
        const loadedDrill = drillResult.value as PronunciationDrill;
        setDrill(loadedDrill);
        analytics.track('pronunciation_drill_viewed', { drillId: loadedDrill.id });
      } else {
        setError('Could not load drill.');
      }
      if (progressResult.status === 'fulfilled') {
        const items = progressResult.value as PronunciationProgressItem[];
        setProgressMap(Object.fromEntries(items.map(p => [p.phonemeCode, p])));
      }
      if (entitlementResult.status === 'fulfilled') {
        setEntitlement(entitlementResult.value as PronunciationEntitlement);
      }
      setLoading(false);
    })();
    return () => { cancelled = true; };
  }, [drillId]);

  const handleUpload = useCallback(async (blob: Blob, durationMs: number, _mimeType: string) => {
    if (!drill) return;
    setUploadError(null);
    try {
      const init = await pronunciationInitAttempt(drill.id);
      const attemptId = (init as { attemptId: string }).attemptId;
      const score = await pronunciationUploadAudio(drill.id, attemptId, blob, { durationMs });
      setAssessment(score as AssessmentResponse);
      analytics.track('pronunciation_attempt_scored', {
        drillId: drill.id,
        phoneme: drill.targetPhoneme,
        overall: (score as AssessmentResponse).overall,
      });
      // Refresh progress + entitlement in the background
      void fetchMyPronunciationProgress()
        .then((items) => setProgressMap(Object.fromEntries((items as PronunciationProgressItem[]).map(p => [p.phonemeCode, p]))))
        .catch(() => { /* swallow */ });
      void fetchPronunciationEntitlement()
        .then((e) => setEntitlement(e as PronunciationEntitlement))
        .catch(() => { /* swallow */ });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not score the recording.';
      setUploadError(message);
      throw err; // let the panel show its own banner too
    }
  }, [drill]);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-8 w-48 mb-4" />
        <Skeleton className="h-64 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!drill) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Drill not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const exampleWords = parseStringArray(drill.exampleWordsJson);
  const minimalPairs = parseMinimalPairs(drill.minimalPairsJson);
  const sentences = parseStringArray(drill.sentencesJson);
  const progress = progressMap[drill.targetPhoneme] ?? null;
  const recordingBlocked = entitlement !== null && !entitlement.allowed;

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link
          href="/pronunciation"
          aria-label="Back to pronunciation drills"
          className="text-muted/60 hover:text-muted"
        >
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div>
          <div className="flex items-center gap-2 text-xs text-muted/60 mb-0.5">
            <Mic className="w-3.5 h-3.5 text-rose-500" aria-hidden />
            <span className="capitalize">{drill.difficulty}</span>
            <span className="font-mono bg-background-light px-1.5 rounded text-muted">
              {drill.targetPhoneme}
            </span>
            {drill.primaryRuleId && (
              <span className="font-mono bg-primary/10 text-primary px-1.5 rounded">
                {drill.primaryRuleId}
              </span>
            )}
          </div>
          <h1 className="text-2xl font-bold text-navy">{drill.label}</h1>
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}
      {uploadError && <InlineAlert variant="warning" className="mb-4">{uploadError}</InlineAlert>}

      <div className="max-w-3xl mx-auto space-y-6">
        {recordingBlocked && entitlement && (
          <InlineAlert variant="warning" title="Free-tier limit reached">
            {entitlement.reason}
            {entitlement.resetAt && (
              <> Resets {new Date(entitlement.resetAt).toLocaleDateString()}.</>
            )}
            <Link href="/billing" className="ml-1 underline">Upgrade</Link>
          </InlineAlert>
        )}
        {entitlement && entitlement.allowed && entitlement.tier === 'free' && entitlement.remaining < 5 && (
          <InlineAlert variant="info">
            {entitlement.remaining} of {entitlement.limitPerWindow} free pronunciation attempts remaining this week.
          </InlineAlert>
        )}

        {progress && (
          <div className="grid grid-cols-3 gap-3 rounded-xl border border-border bg-surface p-4 text-center shadow-sm">
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted">Average score</div>
              <div className="mt-1 text-lg font-semibold text-navy">{Math.round(progress.averageScore)}%</div>
            </div>
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted">Attempts</div>
              <div className="mt-1 text-lg font-semibold text-navy">{progress.attemptCount}</div>
            </div>
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted">Last practiced</div>
              <div className="mt-1 text-xs font-medium text-muted">
                {progress.lastPracticedAt ? new Date(progress.lastPracticedAt).toLocaleDateString() : 'Not yet'}
              </div>
            </div>
          </div>
        )}

        {drill.audioModelUrl && (
          <div className="bg-surface rounded-xl border border-border p-4 flex items-center gap-4">
            <div className="p-3 bg-rose-50 rounded-lg">
              <Volume2 className="w-6 h-6 text-rose-600" aria-hidden />
            </div>
            <div className="flex-1">
              <label htmlFor="model-audio" className="text-sm font-medium text-navy">
                Model audio
              </label>
              <audio id="model-audio" controls src={drill.audioModelUrl} className="mt-2 w-full h-8" />
            </div>
          </div>
        )}

        {exampleWords.length > 0 && (
          <section aria-labelledby="example-words-heading">
            <h2 id="example-words-heading" className="text-lg font-semibold text-navy mb-3">
              Example words
            </h2>
            <div className="flex flex-wrap gap-2">
              {exampleWords.map((word, i) => (
                <MotionItem
                  key={`${word}-${i}`}
                  delayIndex={i}
                  className="rounded-lg border border-border bg-surface px-3 py-1.5 text-sm font-medium text-navy shadow-sm"
                >
                  {word}
                </MotionItem>
              ))}
            </div>
          </section>
        )}

        {minimalPairs.length > 0 && (
          <section aria-labelledby="minimal-pairs-heading">
            <h2 id="minimal-pairs-heading" className="text-lg font-semibold text-navy mb-3">
              Minimal pairs
            </h2>
            <p className="text-xs text-muted mb-2">Each pair differs by one phoneme. Practise to stop merging them.</p>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {minimalPairs.map((pair, i) => (
                <div key={`${pair.a}-${pair.b}-${i}`}
                     className="flex items-center gap-2 rounded-lg border border-border bg-surface p-3 text-sm shadow-sm">
                  <span className="font-medium text-rose-600" aria-label={`First: ${pair.a}`}>
                    {pair.a}
                  </span>
                  <span className="text-xs uppercase text-muted">vs</span>
                  <span className="font-medium text-info" aria-label={`Second: ${pair.b}`}>
                    {pair.b}
                  </span>
                </div>
              ))}
            </div>
            <Link
              href={`/pronunciation/discrimination/${drill.id}`}
              className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            >
              <Play className="h-3.5 w-3.5" aria-hidden /> Play the listening discrimination game
            </Link>
          </section>
        )}

        {sentences.length > 0 && (
          <section aria-labelledby="sentences-heading">
            <h2 id="sentences-heading" className="text-lg font-semibold text-navy mb-3">
              Practice sentences
            </h2>
            <div className="space-y-2">
              {sentences.map((sentence, i) => (
                <div key={`${sentence}-${i}`}
                     className="rounded-lg border border-border bg-surface p-3 text-sm italic text-navy shadow-sm">
                  {sentence}
                </div>
              ))}
            </div>
          </section>
        )}

        {drill.tipsHtml && (
          <section aria-labelledby="tips-heading"
                   className="rounded-2xl border border-rose-200 bg-rose-50/80 p-5 shadow-sm">
            <h3 id="tips-heading" className="mb-3 flex items-center gap-2 text-sm font-semibold text-rose-800">
              <AlertTriangle className="h-4 w-4" aria-hidden /> Articulation tips
            </h3>
            <div
              className="prose prose-sm max-w-none text-rose-950 prose-p:my-2 prose-strong:text-rose-900"
              /* Admin-authored HTML; trusted source. */
              dangerouslySetInnerHTML={{ __html: drill.tipsHtml }}
            />
          </section>
        )}

        {/* The single biggest addition: actually record + score. */}
        <PronunciationRecorderPanel
          onUpload={handleUpload}
          disabled={recordingBlocked}
          modelAudioUrl={drill.audioModelUrl}
        />

        {assessment && (
          <section aria-labelledby="results-heading" className="space-y-4">
            <h2 id="results-heading" className="sr-only">Your pronunciation results</h2>
            <PronunciationResultsCard
              accuracy={assessment.accuracy}
              fluency={assessment.fluency}
              completeness={assessment.completeness}
              prosody={assessment.prosody}
              overall={assessment.overall}
              projectedSpeakingScaled={assessment.projectedSpeakingScaled}
              projectedSpeakingGrade={assessment.projectedSpeakingGrade}
              wordScoresJson={assessment.wordScoresJson}
              problematicPhonemesJson={assessment.problematicPhonemesJson}
              fluencyMarkersJson={assessment.fluencyMarkersJson}
              feedbackJson={assessment.feedbackJson}
              provider={assessment.provider}
              drillId={drill.id}
            />
          </section>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
