'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { CheckCircle2, CircleAlert, Headphones, Play, ShieldAlert } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import type { SpeakingTranscriptPayload } from '@/lib/api/speaking-sessions';
import {
  speakingSimulationV11AudioPath,
  type SpeakingSimulationV11AssessmentResponse,
  type SpeakingSimulationV11CardBreakdown,
  type SpeakingSimulationV11Criterion,
  type SpeakingSimulationV11Evidence,
  type SpeakingSimulationV11LearnerTutorOverride,
} from '@/lib/api/speaking-simulation-v11';

interface SpeakingSimulationV11ReportViewProps {
  sessionId: string;
  response: SpeakingSimulationV11AssessmentResponse;
  transcript?: SpeakingTranscriptPayload | null;
  transcriptsBySessionId?: Record<string, SpeakingTranscriptPayload | null>;
  tutorOverride?: SpeakingSimulationV11LearnerTutorOverride | null;
  tutorOverridesBySessionId?: Record<string, SpeakingSimulationV11LearnerTutorOverride | null>;
  title?: string;
}

const scoreColour = (score: number) =>
  score >= 80 ? 'bg-emerald-500' : score >= 60 ? 'bg-sky-500' : score >= 40 ? 'bg-amber-500' : 'bg-rose-500';

function formatTime(ms: number | null): string {
  if (ms == null || !Number.isFinite(ms)) return '—';
  const seconds = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function JsonSummary({ value }: { value: Record<string, unknown> }) {
  const entries = Object.entries(value).filter(([, item]) => item !== null && item !== '');
  if (entries.length === 0) return <p className="text-sm text-muted">No additional detail recorded.</p>;
  return (
    <dl className="grid gap-2 sm:grid-cols-2">
      {entries.map(([key, item]) => (
        <div key={key} className="rounded-lg border border-border bg-background-light px-3 py-2">
          <dt className="text-[11px] font-semibold uppercase tracking-wide text-muted">
            {key.replace(/([A-Z])/g, ' $1')}
          </dt>
          <dd className="mt-1 text-sm text-foreground">
            {typeof item === 'object' ? JSON.stringify(item) : String(item)}
          </dd>
        </div>
      ))}
    </dl>
  );
}

function EvidenceAudioButton({
  sessionId,
  evidence,
}: {
  sessionId: string;
  evidence: SpeakingSimulationV11Evidence;
}) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const objectUrlRef = useRef<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => () => {
    audioRef.current?.pause();
    if (objectUrlRef.current) URL.revokeObjectURL(objectUrlRef.current);
  }, []);

  if ((evidence.evidenceStatus !== 'supported' && evidence.evidenceStatus !== 'teaching_only')
    || !evidence.sourceRecordingId) return null;

  const play = async () => {
    setError(null);
    setLoading(true);
    try {
      if (!audioRef.current) {
        const objectUrl = await fetchAuthorizedObjectUrl(
          speakingSimulationV11AudioPath(sessionId, evidence.sourceRecordingId),
        );
        objectUrlRef.current = objectUrl;
        const audio = new Audio(objectUrl);
        audioRef.current = audio;
        audio.addEventListener('ended', () => setLoading(false));
      }
      const audio = audioRef.current;
      if (!audio) return;
      audio.currentTime = Math.max(0, (evidence.startMs ?? 0) / 1000);
      await audio.play();
      setLoading(false);
    } catch (err) {
      setLoading(false);
      setError(err instanceof Error ? err.message : 'Audio playback failed.');
    }
  };

  return (
    <span className="inline-flex items-center gap-2">
      <Button type="button" size="sm" variant="outline" onClick={() => void play()} disabled={loading}>
        <Play className="mr-1.5 h-3.5 w-3.5" aria-hidden />
        {loading ? 'Loading…' : `Play ${formatTime(evidence.startMs)}`}
      </Button>
      {error ? <span className="text-xs text-rose-600">{error}</span> : null}
    </span>
  );
}

function CriterionCard({
  criterion,
  sessionId,
}: {
  criterion: SpeakingSimulationV11Criterion;
  sessionId: string;
}) {
  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-foreground">{criterion.label}</h3>
          <p className="mt-0.5 text-xs text-muted">
            {criterion.weight}% · {criterion.criterionCode}
          </p>
        </div>
        <span className="text-lg font-bold tabular-nums text-foreground">
          {Math.round(criterion.rawScore)}
          <span className="text-xs font-medium text-muted">/100</span>
        </span>
      </div>
      <div className="mt-3 h-2 overflow-hidden rounded-full bg-slate-100" aria-label={`${criterion.label} score`}>
        <div
          className={cn('h-full rounded-full transition-all', scoreColour(criterion.rawScore))}
          style={{ width: `${Math.max(0, Math.min(100, criterion.rawScore))}%` }}
        />
      </div>
      <p className="mt-3 text-sm leading-relaxed text-muted">{criterion.rationale || 'No rationale recorded.'}</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-3">
        {criterion.strength ? (
          <div className="rounded-lg bg-emerald-50 p-3 text-xs text-emerald-900">
            <p className="font-semibold">Strength</p>
            <p className="mt-1">{criterion.strength}</p>
          </div>
        ) : null}
        {criterion.weakness ? (
          <div className="rounded-lg bg-amber-50 p-3 text-xs text-amber-900">
            <p className="font-semibold">Growth area</p>
            <p className="mt-1">{criterion.weakness}</p>
          </div>
        ) : null}
        {criterion.action ? (
          <div className="rounded-lg bg-sky-50 p-3 text-xs text-sky-900">
            <p className="font-semibold">Next action</p>
            <p className="mt-1">{criterion.action}</p>
          </div>
        ) : null}
      </div>
      {criterion.evidence.length > 0 ? (
        <div className="mt-4 space-y-2 border-t border-border pt-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted">Source-linked evidence</p>
          {criterion.evidence.map((evidence, index) => (
            <div
              key={`${criterion.criterionCode}-${index}-${evidence.turnNumber ?? 'unlinked'}`}
              className={cn(
                'rounded-lg border p-3',
                evidence.evidenceStatus === 'supported'
                  ? 'border-border bg-background-light'
                  : 'border-dashed border-amber-300 bg-amber-50/60',
              )}
            >
              <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
                <span className="font-semibold">{evidence.evidenceType}</span>
                {evidence.turnNumber ? <span>Turn {evidence.turnNumber}</span> : null}
                {evidence.startMs != null ? <span>{formatTime(evidence.startMs)}–{formatTime(evidence.endMs)}</span> : null}
                <span className="rounded-full bg-white px-2 py-0.5">
                  {evidence.evidenceStatus === 'supported'
                    ? evidence.isPrimary ? 'verified score source' : 'verified teaching source'
                    : evidence.evidenceStatus === 'teaching_only' ? 'teaching-only; not score-affecting' : 'not source-verified'}
                </span>
              </div>
              {evidence.quoteText ? (
                <blockquote className="mt-2 border-l-2 border-sky-300 pl-3 text-sm italic text-foreground">
                  “{evidence.quoteText}”
                </blockquote>
              ) : null}
              {evidence.finding ? <p className="mt-2 text-sm text-muted">{evidence.finding}</p> : null}
              {evidence.action ? <p className="mt-1 text-xs font-medium text-sky-800">Try: {evidence.action}</p> : null}
              <div className="mt-2">
                <EvidenceAudioButton sessionId={sessionId} evidence={evidence} />
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </Card>
  );
}

function CardBreakdownCard({
  breakdown,
  tutorOverride,
}: {
  breakdown: SpeakingSimulationV11CardBreakdown;
  tutorOverride?: SpeakingSimulationV11LearnerTutorOverride | null;
}) {
  return (
    <Card className="p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-foreground">
            Card {breakdown.cardSlot.toUpperCase()}
          </h2>
          <p className="mt-1 text-xs text-muted">
            Confidence: <span className="font-semibold text-foreground">{breakdown.confidenceLabel}</span>
            {' · '}Range: <span className="font-semibold text-foreground">
              {breakdown.scoreRangeLow ?? '—'}–{breakdown.scoreRangeHigh ?? '—'}
            </span>
          </p>
        </div>
        <div className="text-right">
          <span className="text-2xl font-black tabular-nums text-foreground">
            {breakdown.estimatedPracticeScore ?? '—'}
          </span>
          <span className="ml-1 text-xs text-muted">/ 500</span>
        </div>
      </div>
      <div className="mt-4 grid gap-3 md:grid-cols-2">
        {breakdown.criteria.map((criterion) => (
          <CriterionCard
            key={`${breakdown.cardSlot}-${criterion.criterionCode}`}
            criterion={criterion}
            sessionId={breakdown.speakingSessionId}
          />
        ))}
      </div>
      {tutorOverride ? (
        <div className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 p-3">
          <p className="text-xs font-semibold uppercase tracking-wide text-emerald-800">Human tutor revision</p>
          <p className="mt-1 text-sm font-semibold text-emerald-950">{tutorOverride.estimatedPracticeScore} / 500 · range {tutorOverride.scoreRangeLow}-{tutorOverride.scoreRangeHigh}</p>
          <p className="mt-1 text-xs text-emerald-900">{tutorOverride.reason}</p>
        </div>
      ) : null}
      <div className="mt-4 grid gap-3 lg:grid-cols-2">
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Strengths</h3>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted">
            {(breakdown.strengths.length ? breakdown.strengths : ['No strengths recorded.']).map((item) => <li key={item}>{item}</li>)}
          </ul>
        </Card>
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Weaknesses and priorities</h3>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted">
            {(breakdown.weaknesses.length ? breakdown.weaknesses : breakdown.topFive).map((item) => <li key={item}>{item}</li>)}
          </ul>
        </Card>
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Task map</h3>
          <div className="mt-2 space-y-2">
            {breakdown.taskMap.length ? breakdown.taskMap.map((task) => (
              <div key={task.taskNumber} className="rounded-lg border border-border p-2 text-sm">
                <span className="font-semibold">Task {task.taskNumber}:</span> {task.status}
                {task.evidence ? <p className="mt-1 text-xs text-muted">{task.evidence}</p> : null}
              </div>
            )) : <p className="text-sm text-muted">No task map recorded.</p>}
          </div>
        </Card>
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Communication timeline</h3>
          <div className="mt-2 space-y-2">
            {breakdown.timeline.length ? breakdown.timeline.map((item, index) => (
              <div key={`${item.label}-${index}`} className="flex gap-2 text-sm">
                <span className="w-24 flex-none font-mono text-xs text-muted">{formatTime(item.startMs)} - {formatTime(item.endMs)}</span>
                <div><p className="font-medium text-foreground">{item.label}</p>{item.note ? <p className="text-xs text-muted">{item.note}</p> : null}</div>
              </div>
            )) : <p className="text-sm text-muted">No timeline recorded.</p>}
          </div>
        </Card>
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Language and time management</h3>
          <div className="mt-2 space-y-3">
            <JsonSummary value={breakdown.languageAnalysis} />
            <JsonSummary value={breakdown.timeManagement} />
          </div>
        </Card>
        <Card className="p-4">
          <h3 className="text-sm font-semibold text-foreground">Better alternatives and tips</h3>
          <div className="mt-2 space-y-2">
            {breakdown.betterAlternatives.map((item, index) => (
              <div key={`${item.turnNumber}-${index}`} className="rounded-lg border border-border p-2 text-sm">
                <p className="italic text-muted">&quot;{item.originalQuote}&quot;</p>
                <p className="mt-1 font-medium text-sky-800">&quot;{item.betterAlternative}&quot;</p>
              </div>
            ))}
            <ul className="list-disc space-y-1 pl-5 text-sm text-muted">
              {(breakdown.tips.length ? breakdown.tips : ['No additional tips recorded.']).map((tip) => <li key={tip}>{tip}</li>)}
            </ul>
          </div>
        </Card>
        <Card className="p-4 lg:col-span-2">
          <h3 className="text-sm font-semibold text-foreground">Practice plan</h3>
          <div className="mt-2 grid gap-2 md:grid-cols-2">
            {breakdown.practicePlan.length ? breakdown.practicePlan.map((item, index) => (
              <div key={`${item.focus}-${index}`} className="rounded-lg border border-border p-3 text-sm">
                <p className="font-semibold text-foreground">{item.focus}</p>
                <p className="mt-1 text-muted">{item.action}</p>
                <p className="mt-2 text-xs text-sky-800">{item.frequency} - Success: {item.successMeasure}</p>
              </div>
            )) : <p className="text-sm text-muted">No practice plan recorded.</p>}
          </div>
        </Card>
      </div>
    </Card>
  );
}

export function SpeakingSimulationV11ReportView({
  sessionId,
  response,
  transcript,
  transcriptsBySessionId,
  tutorOverride,
  tutorOverridesBySessionId,
  title = 'Speaking simulation report',
}: SpeakingSimulationV11ReportViewProps) {
  const report = response.report;
  const score = report?.estimatedPracticeScore ?? response.estimatedPracticeScore;
  const rangeLow = report?.scoreRangeLow ?? response.scoreRangeLow;
  const rangeHigh = report?.scoreRangeHigh ?? response.scoreRangeHigh;
  const disclaimer = report?.graphDisclaimer || response.graphDisclaimer;
  const [activeTab, setActiveTab] = useState<'overview' | 'criteria' | 'transcript' | 'plan'>('overview');
  const cardBreakdowns = report?.cardBreakdowns ?? [];

  const transcriptSegments = useMemo(() => {
    if (report?.cardSlot === 'combined' && report.cardBreakdowns.length > 0) {
      return report.cardBreakdowns.flatMap((breakdown) =>
        (transcriptsBySessionId?.[breakdown.speakingSessionId]?.segments ?? []).map((segment) => ({
          ...segment,
          cardSlot: breakdown.cardSlot,
        })),
      );
    }
    return (transcript?.segments ?? []).map((segment) => ({ ...segment, cardSlot: undefined }));
  }, [report, transcript, transcriptsBySessionId]);

  if (response.status === 'TechnicalReview' || !report) {
    return (
      <Card className="border-amber-300 bg-amber-50 p-5">
        <div className="flex items-start gap-3">
          <ShieldAlert className="mt-0.5 h-5 w-5 flex-none text-amber-700" aria-hidden />
          <div>
            <h2 className="font-semibold text-amber-950">Technical review required</h2>
            <p className="mt-1 text-sm leading-relaxed text-amber-900">
              This attempt has no estimated score because the authoritative audio, transcript, or assessment pipeline
              could not be verified. The issue must be reviewed or the controlled retake path used.
            </p>
            {response.technicalReviewCode ? (
              <p className="mt-2 text-xs font-semibold uppercase tracking-wide text-amber-800">
                Reason: {response.technicalReviewCode}
              </p>
            ) : null}
          </div>
        </div>
      </Card>
    );
  }

  const scorePercent = score == null ? 0 : Math.max(0, Math.min(100, score / 5));

  return (
    <div className="space-y-5">
      <Card className="overflow-hidden border-slate-200 bg-white">
        <div className="flex flex-col gap-5 p-5 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.16em] text-sky-700">{title}</p>
            <h1 className="mt-1 text-2xl font-bold text-foreground">AI Estimated Practice Score</h1>
            <p className="mt-2 max-w-xl text-sm leading-relaxed text-muted">
              This is a calibrated practice estimate based on the released v1.1 rubric and source-linked evidence.
            </p>
            <p className="mt-3 inline-flex items-center gap-2 rounded-md bg-slate-100 px-3 py-2 text-xs font-semibold text-slate-700">
              <CircleAlert className="h-4 w-4" aria-hidden />
              {disclaimer || 'AI Estimated Practice Score — not an official OET result'}
            </p>
          </div>
          <div className="flex items-center gap-4">
            <div
              className="grid h-32 w-32 place-items-center rounded-full"
              style={{ background: `conic-gradient(#0284c7 ${scorePercent}%, #e2e8f0 0)` }}
              role="img"
              aria-label={`Estimated practice score ${score ?? 'unavailable'} out of 500`}
            >
              <div className="grid h-24 w-24 place-items-center rounded-full bg-white text-center">
                <span className="text-3xl font-black tabular-nums text-slate-900">{score ?? '—'}</span>
                <span className="text-[11px] font-semibold text-muted">/ 500</span>
              </div>
            </div>
            <div className="text-sm">
              <p className="font-semibold text-foreground">{report.cardSlot === 'combined' ? 'Full mock' : `Card ${report.cardSlot.toUpperCase()}`}</p>
              <p className="mt-1 text-muted">Confidence: <span className="font-semibold text-foreground">{report.confidenceLabel}</span></p>
              <p className="text-muted">Range: <span className="font-semibold text-foreground">{rangeLow ?? '—'}–{rangeHigh ?? '—'}</span></p>
            </div>
          </div>
        </div>
        <nav className="flex gap-1 overflow-x-auto border-t border-border px-4" aria-label="Report sections">
          {(['overview', 'criteria', 'transcript', 'plan'] as const).map((tab) => (
            <button
              key={tab}
              type="button"
              className={cn(
                'border-b-2 px-3 py-3 text-sm font-medium capitalize',
                activeTab === tab ? 'border-sky-600 text-sky-700' : 'border-transparent text-muted hover:text-foreground',
              )}
              onClick={() => setActiveTab(tab)}
            >
              {tab}
            </button>
          ))}
        </nav>
      </Card>

      {tutorOverride ? (
        <Card className="border-emerald-200 bg-emerald-50 p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.16em] text-emerald-800">Human tutor revision</p>
          <div className="mt-2 flex flex-wrap items-baseline gap-x-3 gap-y-1">
            <p className="text-xl font-bold text-emerald-950">{tutorOverride.estimatedPracticeScore} / 500</p>
            <p className="text-sm text-emerald-900">Reviewed range: {tutorOverride.scoreRangeLow}-{tutorOverride.scoreRangeHigh}</p>
          </div>
          <p className="mt-2 text-sm leading-relaxed text-emerald-900">{tutorOverride.reason}</p>
          <p className="mt-2 text-xs text-emerald-800">The AI report above remains preserved as the original practice estimate; this human revision is shown separately.</p>
        </Card>
      ) : null}

      {report.cardSlot === 'combined' && cardBreakdowns.length > 0 ? (
        <section className="space-y-3" aria-label="Per-card breakdowns">
          <div>
            <h2 className="text-lg font-semibold text-foreground">Card breakdowns</h2>
            <p className="mt-1 text-sm text-muted">
              The combined estimate is derived only from these two independently valid card reports.
            </p>
          </div>
          <div className="space-y-4">
            {cardBreakdowns.map((breakdown) => (
              <CardBreakdownCard
                key={`${breakdown.cardSlot}-${breakdown.assessmentId}`}
                breakdown={breakdown}
                tutorOverride={tutorOverridesBySessionId?.[breakdown.speakingSessionId]}
              />
            ))}
          </div>
        </section>
      ) : null}

      {activeTab === 'overview' ? (
        <div className="space-y-5">
          {report.overallSummary ? <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Summary</h2><p className="mt-2 text-sm leading-relaxed text-muted">{report.overallSummary}</p></Card> : null}
          <div className="grid gap-5 lg:grid-cols-2">
            <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Strengths</h2><ul className="mt-3 space-y-2 text-sm text-muted">{report.strengths.map((item) => <li key={item} className="flex gap-2"><CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" aria-hidden />{item}</li>)}</ul></Card>
            <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Top improvements</h2><ol className="mt-3 list-decimal space-y-2 pl-5 text-sm text-muted">{(report.topFive.length ? report.topFive : report.weaknesses).map((item) => <li key={item}>{item}</li>)}</ol></Card>
          </div>
          <div className="grid gap-5 lg:grid-cols-2">
            <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Task map</h2><div className="mt-3 space-y-2">{report.taskMap.map((task) => <div key={task.taskNumber} className="flex gap-3 rounded-lg border border-border p-3 text-sm"><span className="font-semibold">{task.taskNumber}</span><div><span className="font-medium text-foreground">{task.status}</span>{task.evidence ? <p className="mt-1 text-xs text-muted">{task.evidence}</p> : null}</div></div>)}</div></Card>
            <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Communication timeline</h2><div className="mt-3 space-y-2">{report.timeline.map((item, index) => <div key={`${item.label}-${index}`} className="flex gap-3 text-sm"><span className="w-24 flex-none font-mono text-xs text-muted">{formatTime(item.startMs)}–{formatTime(item.endMs)}</span><div><p className="font-medium text-foreground">{item.label}</p>{item.note ? <p className="text-xs text-muted">{item.note}</p> : null}</div></div>)}</div></Card>
          </div>
        </div>
      ) : null}

      {activeTab === 'criteria' ? (
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            {report.criteria.map((criterion) => <CriterionCard key={criterion.criterionCode} criterion={criterion} sessionId={sessionId} />)}
          </div>
        </div>
      ) : null}

      {activeTab === 'transcript' ? (
        <Card className="p-5">
          <div className="flex items-center gap-2"><Headphones className="h-5 w-5 text-sky-700" aria-hidden /><h2 className="text-base font-semibold text-foreground">Transcript and source audio</h2></div>
          <p className="mt-2 text-sm text-muted">Audio playback is available only for source-linked evidence and remains protected by learner authorization.</p>
          <p className="mt-2 text-xs text-muted">Warm-up is unscored. Speaker labels, timestamps, word counts, and ASR confidence are shown from the authoritative transcript; ASR uncertainty is not treated as candidate error.</p>
          <div className="mt-4 space-y-3">{transcriptSegments.length ? transcriptSegments.map((segment, index) => {
            const speakerCode = segment.speaker.trim().toLowerCase();
            const speaker = speakerCode === 'candidate' || speakerCode === 'learner'
              ? 'Candidate'
              : ['interlocutor', 'patient', 'relative', 'layperson', 'lay_person'].includes(speakerCode)
                ? 'Patient / interlocutor'
                : segment.speaker;
            const wordCount = Array.isArray(segment.words) && segment.words.length > 0
              ? segment.words.length
              : segment.text.trim() ? segment.text.trim().split(/\s+/).length : 0;
            const confidence = typeof segment.confidence === 'number' && Number.isFinite(segment.confidence)
              ? `${Math.round(segment.confidence * 100)}%`
              : 'not available';
            const words = Array.isArray(segment.words) ? segment.words : [];
            return <div key={`${segment.startMs}-${segment.endMs}-${index}`} className="rounded-lg border border-border p-3">
              <div className="flex flex-wrap justify-between gap-2 text-xs text-muted"><span className="font-semibold text-foreground">{speaker}</span><span>{formatTime(segment.startMs)}-{formatTime(segment.endMs)} · {wordCount} words · ASR confidence {confidence}</span></div>
              <p className="mt-1 text-sm text-foreground">{segment.text || 'No recognized words.'}</p>
              {words.length > 0 ? (
                <details className="mt-2 rounded-md bg-slate-50 px-2 py-1 text-xs text-muted">
                  <summary className="cursor-pointer font-semibold">Word confidence and timestamps</summary>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {words.map((word, wordIndex) => (
                      <span key={`${word.word}-${word.startMs}-${wordIndex}`} className="rounded bg-white px-1.5 py-1" title={`${formatTime(word.startMs)}-${formatTime(word.endMs)}`}>
                        {word.word} ({Math.round(Math.max(0, Math.min(1, word.confidence)) * 100)}%)
                      </span>
                    ))}
                  </div>
                </details>
              ) : null}
            </div>;
          }) : <p className="text-sm text-muted">The transcript is not available yet.</p>}</div>
        </Card>
      ) : null}

      {activeTab === 'plan' ? (
        <div className="space-y-5">
          <div className="grid gap-5 lg:grid-cols-2"><Card className="p-5"><h2 className="text-base font-semibold text-foreground">Language analysis</h2><div className="mt-3"><JsonSummary value={report.languageAnalysis} /></div></Card><Card className="p-5"><h2 className="text-base font-semibold text-foreground">Time management</h2><div className="mt-3"><JsonSummary value={report.timeManagement} /></div></Card></div>
          <Card className="p-5"><h2 className="text-base font-semibold text-foreground">Better alternatives</h2><div className="mt-3 grid gap-3 md:grid-cols-2">{report.betterAlternatives.map((item, index) => <div key={`${item.turnNumber}-${index}`} className="rounded-lg border border-border p-3 text-sm"><p className="italic text-muted">“{item.originalQuote}”</p><p className="mt-2 font-medium text-sky-800">“{item.betterAlternative}”</p><p className="mt-2 text-xs text-muted">Turn {item.turnNumber ?? '—'} · {formatTime(item.startMs)}–{formatTime(item.endMs)}</p></div>)}</div></Card>
          <div className="grid gap-5 lg:grid-cols-2"><Card className="p-5"><h2 className="text-base font-semibold text-foreground">Tips</h2><ul className="mt-3 list-disc space-y-2 pl-5 text-sm text-muted">{report.tips.map((tip) => <li key={tip}>{tip}</li>)}</ul></Card><Card className="p-5"><h2 className="text-base font-semibold text-foreground">Practice plan</h2><div className="mt-3 space-y-3">{report.practicePlan.map((item, index) => <div key={`${item.focus}-${index}`} className="rounded-lg border border-border p-3 text-sm"><p className="font-semibold text-foreground">{item.focus}</p><p className="mt-1 text-muted">{item.action}</p><p className="mt-2 text-xs text-sky-800">{item.frequency} · Success: {item.successMeasure}</p></div>)}</div></Card></div>
        </div>
      ) : null}
    </div>
  );
}
