'use client';

import { useMemo } from 'react';
import { CheckCircle2, ChevronRight, Info } from 'lucide-react';
import Link from 'next/link';
import { pronunciationScoreTier } from '@/lib/scoring';
import { PhonemeHeatmap } from './PhonemeHeatmap';

type ProblematicPhoneme = {
  phoneme: string;
  score: number;
  occurrences: number;
  ruleId?: string | null;
};

type FluencyMarkers = {
  speechRateWpm?: number;
  pauseCount?: number;
  averagePauseDurationMs?: number;
};

type Feedback = {
  summary?: string;
  strengths?: string[];
  improvements?: Array<{ ruleId: string; message: string; drillSuggestion?: string | null }>;
  appliedRuleIds?: string[];
  nextDrillTargetPhoneme?: string | null;
};

export type PronunciationResultsCardProps = {
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
  drillId: string;
};

/**
 * Learner-facing results card for a pronunciation attempt. Keeps all scoring
 * information in one place:
 *   - 4 primary score bars (accuracy / fluency / completeness / prosody)
 *   - Overall composite + projected Speaking band (advisory)
 *   - Per-word heatmap (green/amber/red with text label, not colour-only)
 *   - Problematic phonemes with rule IDs
 *   - Fluency markers (speech rate, pause count)
 *   - Grounded-AI coaching panel
 *
 * Never compares `overall >= 70` directly; all pass/fail / grade presentation
 * comes from the backend's projected Speaking fields, which are themselves
 * derived from `OetScoring.PronunciationProjectedBand`.
 */
export function PronunciationResultsCard(props: PronunciationResultsCardProps) {
  const words = useMemo(() => safeJsonArray(props.wordScoresJson), [props.wordScoresJson]);
  const phonemes = useMemo<ProblematicPhoneme[]>(() => safeJsonArray(props.problematicPhonemesJson), [props.problematicPhonemesJson]);
  const fluency: FluencyMarkers = useMemo(() => safeJsonObject(props.fluencyMarkersJson), [props.fluencyMarkersJson]);
  const feedback: Feedback = useMemo(() => safeJsonObject(props.feedbackJson), [props.feedbackJson]);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-5">
        <ScoreTile label="Accuracy" value={props.accuracy} hint="Phoneme articulation" />
        <ScoreTile label="Fluency" value={props.fluency} hint="Pacing & smoothness" />
        <ScoreTile label="Completeness" value={props.completeness} hint="Words delivered" />
        <ScoreTile label="Prosody" value={props.prosody} hint="Stress & intonation" />
        <ScoreTile label="Overall" value={props.overall} hint="Composite" highlight />
      </div>

      <div className="rounded-3xl border border-border bg-surface p-5 shadow-sm">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="text-xs uppercase tracking-[0.18em] text-muted">Projected OET Speaking band</div>
            <div className="text-2xl font-semibold text-navy dark:text-white">
              {props.projectedSpeakingScaled}/500 · Grade {props.projectedSpeakingGrade}
            </div>
            <p className="mt-1 max-w-lg text-xs text-muted">
              Advisory projection from this pronunciation attempt. The authoritative Speaking band
              is set by tutor review of a full Speaking role-play — pass is 350/500 (universal).
            </p>
          </div>
          <Info className="h-5 w-5 shrink-0 text-muted" aria-hidden />
        </div>
      </div>

      <section aria-labelledby="word-heatmap-heading" className="rounded-3xl border border-border bg-surface p-5 shadow-sm">
        <h3 id="word-heatmap-heading" className="mb-3 text-sm font-semibold text-navy dark:text-white">
          Word-level accuracy
        </h3>
        <PhonemeHeatmap wordScores={words as Array<{ word: string; accuracyScore: number; errorType: string }>} />
      </section>

      {phonemes.length > 0 && (
        <section aria-labelledby="phoneme-issues-heading" className="rounded-3xl border border-border bg-surface p-5 shadow-sm">
          <h3 id="phoneme-issues-heading" className="mb-3 text-sm font-semibold text-navy dark:text-white">
            Phonemes that need work
          </h3>
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {phonemes.map((p, i) => (
              <li key={`${p.phoneme}-${i}`} className="flex items-center justify-between rounded-2xl border border-border p-3">
                <div className="flex items-center gap-3">
                  <span className="font-mono text-lg font-semibold text-navy dark:text-white">/{p.phoneme}/</span>
                  <span className="text-xs text-muted">
                    {p.occurrences} occurrence{p.occurrences !== 1 ? 's' : ''}
                    {p.ruleId ? ` · ${p.ruleId}` : ''}
                  </span>
                </div>
                <span className={`font-mono text-sm font-semibold ${tintForScore(p.score)}`}>
                  {Math.round(p.score)}%
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {(fluency.speechRateWpm || fluency.pauseCount) && (
        <section aria-labelledby="fluency-heading" className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {typeof fluency.speechRateWpm === 'number' && (
            <StatTile label="Speech rate" value={`${Math.round(fluency.speechRateWpm)} wpm`} hint="Target 140–170 wpm for clinical speech" />
          )}
          {typeof fluency.pauseCount === 'number' && (
            <StatTile label="Pauses" value={`${fluency.pauseCount}`} hint="Clause boundaries only" />
          )}
          {typeof fluency.averagePauseDurationMs === 'number' && (
            <StatTile label="Avg. pause" value={`${Math.round(fluency.averagePauseDurationMs)} ms`} hint="300–500 ms is natural" />
          )}
        </section>
      )}

      {(feedback.summary || (feedback.improvements?.length ?? 0) > 0) && (
        <section aria-labelledby="coaching-heading" className="rounded-3xl border border-primary/30 bg-primary/5 p-5 shadow-sm">
          <h3 id="coaching-heading" className="mb-2 text-sm font-semibold text-navy dark:text-white">
            Targeted coaching
          </h3>
          {feedback.summary && <p className="text-sm text-navy dark:text-white/90">{feedback.summary}</p>}

          {(feedback.strengths?.length ?? 0) > 0 && (
            <div className="mt-3">
              <div className="text-xs uppercase tracking-[0.15em] text-muted">Strengths</div>
              <ul className="mt-1 space-y-1 text-sm text-navy dark:text-white/90">
                {feedback.strengths!.map((s, i) => (
                  <li key={i} className="flex items-start gap-2">
                    <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" aria-hidden />
                    <span>{s}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {(feedback.improvements?.length ?? 0) > 0 && (
            <div className="mt-3">
              <div className="text-xs uppercase tracking-[0.15em] text-muted">Work on next</div>
              <ul className="mt-1 space-y-2 text-sm text-navy dark:text-white/90">
                {feedback.improvements!.map((imp, i) => (
                  <li key={`${imp.ruleId}-${i}`} className="rounded-2xl border border-border bg-surface p-3">
                    <div className="flex items-center gap-2 text-xs text-muted">
                      <span className="rounded-full bg-background-light px-2 py-0.5 font-mono">{imp.ruleId}</span>
                    </div>
                    <p className="mt-1">{imp.message}</p>
                    {imp.drillSuggestion && (
                      <p className="mt-1 text-xs text-muted">Suggested: {imp.drillSuggestion}</p>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}

          {feedback.nextDrillTargetPhoneme && (
            <Link
              href={`/pronunciation?focus=phoneme&phoneme=${encodeURIComponent(feedback.nextDrillTargetPhoneme)}`}
              className="mt-3 inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            >
              Find drills for /{feedback.nextDrillTargetPhoneme}/ <ChevronRight className="h-4 w-4" />
            </Link>
          )}
        </section>
      )}

      <div className="text-xs text-muted">
        Scoring provider: <span className="font-mono">{props.provider}</span>. Results are advisory;
        authoritative Speaking bands require tutor review.
      </div>
    </div>
  );
}

function ScoreTile({
  label,
  value,
  hint,
  highlight,
}: {
  label: string;
  value: number;
  hint: string;
  highlight?: boolean;
}) {
  return (
    <div
      className={`rounded-2xl border p-4 shadow-sm ${
        highlight ? 'border-primary/50 bg-primary/5' : 'border-border bg-surface'
      }`}
    >
      <div className="text-[11px] uppercase tracking-[0.15em] text-muted">{label}</div>
      <div className={`mt-1 font-mono text-2xl font-semibold ${tintForScore(value)}`}>
        {Math.round(value)}
      </div>
      <div className="mt-1 h-1 w-full overflow-hidden rounded-full bg-background-light">
        <div className={`h-full ${barClass(value)}`} style={{ width: `${Math.max(0, Math.min(100, value))}%` }} />
      </div>
      <div className="mt-1 text-[11px] text-muted">{hint}</div>
    </div>
  );
}

function StatTile({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
      <div className="text-[11px] uppercase tracking-[0.15em] text-muted">{label}</div>
      <div className="mt-1 font-mono text-lg font-semibold text-navy dark:text-white">{value}</div>
      <div className="text-[11px] text-muted">{hint}</div>
    </div>
  );
}

// Advisory colour bucketing delegates the numeric thresholds (85 / 70) to
// `pronunciationScoreTier` in `lib/scoring.ts` so the 70 anchor lives in one place.
function tintForScore(score: number) {
  switch (pronunciationScoreTier(score)) {
    case 'excellent': return 'text-emerald-600 dark:text-emerald-400';
    case 'passing':   return 'text-amber-600 dark:text-amber-400';
    default:          return 'text-rose-600 dark:text-rose-400';
  }
}

function barClass(score: number) {
  switch (pronunciationScoreTier(score)) {
    case 'excellent': return 'bg-emerald-500';
    case 'passing':   return 'bg-amber-500';
    default:          return 'bg-rose-500';
  }
}

function safeJsonArray<T = unknown>(value: string | null | undefined): T[] {
  if (!value) return [];
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed as T[] : [];
  } catch {
    return [];
  }
}

function safeJsonObject<T extends Record<string, unknown>>(value: string | null | undefined): T {
  if (!value) return {} as T;
  try {
    const parsed = JSON.parse(value);
    return (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) ? parsed as T : {} as T;
  } catch {
    return {} as T;
  }
}
