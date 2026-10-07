'use client';

/**
 * Admin · Speaking · Grader calibration · mark one whole Full Mock (owner request 7 Oct 2026).
 *
 * A Full Mock is ONE performance: the candidate speaks Card A and Card B and receives one combined
 * Speaking result. Dr Hesham hears both cards' audio, reads both cards and both cleaned transcripts,
 * then gives ONE set of the nine OET criteria and ONE overall result /500 for the whole test. That
 * single mark is what the combined grader (speaking.score.v4-combined) is compared against. The AI's
 * score is never on this page, so the number being validated cannot anchor his marks.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { EyeOff, Scale } from 'lucide-react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Textarea } from '@/components/admin/ui/textarea';
import { InlineAlert } from '@/components/ui/alert';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { oetReportedGradeFromScaled } from '@/lib/scoring';
import {
  adminExcludeGraderCalibrationMockSample,
  adminGetGraderCalibrationMocks,
  adminGetGraderCalibrationMockSample,
  adminLabelGraderCalibrationMockSample,
  describeAudioCoverage,
  graderCalibrationMockAudioPath,
  type GraderCalibrationAudioClip,
  type GraderCalibrationMockSampleDetail,
} from '@/lib/api/speaking-grader-calibration';

const OVERALL_OPTIONS = Array.from({ length: 51 }, (_, index) => index * 10);

function formatClock(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

/** One clip of the mock, fetched with the admin's own authorisation when the page opens. */
function ClipPlayer({
  sampleId,
  clip,
  index,
  slot,
}: {
  sampleId: string;
  clip: GraderCalibrationAudioClip;
  index: number;
  slot: 'A' | 'B';
}) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    fetchAuthorizedObjectUrl(graderCalibrationMockAudioPath(sampleId, clip.recordingId))
      .then((next) => {
        if (cancelled) {
          URL.revokeObjectURL(next);
          return;
        }
        objectUrl = next;
        setUrl(next);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [sampleId, clip.recordingId]);

  return (
    <div className="space-y-1">
      <p className="text-xs text-admin-fg-muted">
        Card {slot} · clip {index + 1} · {formatClock(clip.durationSeconds * 1000)}
        {clip.linked === false ? ' · not linked to a transcript turn' : ''}
      </p>
      {failed ? (
        <p className="text-xs text-admin-danger" role="alert">This clip could not be loaded.</p>
      ) : url ? (
        <audio controls src={url} className="w-full" aria-label={`Card ${slot} audio clip ${index + 1}`} />
      ) : (
        <Skeleton className="h-10 w-full" />
      )}
    </div>
  );
}

export default function SpeakingGraderCalibrationMockSamplePage() {
  const params = useParams<{ id: string }>();
  const sampleId = params?.id ?? '';

  const [detail, setDetail] = useState<GraderCalibrationMockSampleDetail | null>(null);
  const [scores, setScores] = useState<Record<string, number>>({});
  const [overall, setOverall] = useState<number | null>(null);
  const [notes, setNotes] = useState('');
  const [excludeReason, setExcludeReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [nextId, setNextId] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!sampleId) return;
    setError(null);
    try {
      const next = await adminGetGraderCalibrationMockSample(sampleId);
      setDetail(next);
      setScores(next.label?.scores ?? {});
      setOverall(next.label?.overallScaled ?? null);
      setNotes(next.label?.notes ?? '');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load this Full Mock.');
    }
  }, [sampleId]);

  useEffect(() => {
    void load();
  }, [load]);

  const complete = useMemo(
    () => detail !== null && overall !== null && detail.criteria.every((criterion) => scores[criterion.code] !== undefined),
    [detail, overall, scores],
  );
  const rawTotal = useMemo(
    () => (detail?.criteria ?? []).reduce((sum, criterion) => sum + (scores[criterion.code] ?? 0), 0),
    [detail, scores],
  );
  const rawMax = useMemo(() => (detail?.criteria ?? []).reduce((sum, criterion) => sum + criterion.max, 0), [detail]);

  const findNextPending = async () => {
    try {
      const overview = await adminGetGraderCalibrationMocks();
      setNextId(overview.samples.find((sample) => sample.status === 'pending' && sample.id !== sampleId)?.id ?? null);
    } catch {
      setNextId(null);
    }
  };

  const save = async () => {
    if (!detail || overall === null || !complete) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await adminLabelGraderCalibrationMockSample(detail.id, { scores, overallScaled: overall, notes });
      setSaved(true);
      await findNextPending();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save your marks.');
    } finally {
      setSaving(false);
    }
  };

  const exclude = async () => {
    if (!detail) return;
    if (!window.confirm('Exclude this Full Mock? It will not be used for calibration.')) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await adminExcludeGraderCalibrationMockSample(detail.id, excludeReason);
      setExcludeReason('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not exclude this Full Mock.');
    } finally {
      setSaving(false);
    }
  };

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Speaking', href: '/admin/speaking' },
    { label: 'Grader calibration', href: '/admin/speaking/grader-calibration' },
    { label: 'Mark a Full Mock' },
  ];

  const cardSection = (slot: 'A' | 'B') => {
    if (!detail) return null;
    const card = slot === 'A' ? detail.cardA : detail.cardB;
    const transcript = slot === 'A' ? detail.transcriptA : detail.transcriptB;
    const clips = slot === 'A' ? detail.clipsA : detail.clipsB;
    const coverage = describeAudioCoverage(slot === 'A' ? detail.audioCoverageA : detail.audioCoverageB);
    return (
      <>
        <SettingsSection
          title={`Role-play card ${slot}: ${card.title}`}
          description={`${card.setting} · ${card.candidateRole} with ${card.interlocutorRole}`}
        >
          <div className="space-y-3 text-sm text-admin-fg-default">
            <p className="whitespace-pre-line">{card.background}</p>
            {card.tasks.length > 0 ? (
              <ol className="list-decimal space-y-1 pl-5">
                {card.tasks.map((task, index) => (
                  <li key={index} className="whitespace-pre-line">{task}</li>
                ))}
              </ol>
            ) : null}
          </div>
        </SettingsSection>

        <SettingsSection
          title={`Card ${slot} audio`}
          description={clips.length > 0 ? 'Listen first: intelligibility and fluency are judged from the audio.' : 'No audio clips for this card.'}
        >
          {coverage ? (
            <p className="mb-3 text-sm text-admin-fg-muted" data-testid={`calibration-mock-audio-coverage-${slot.toLowerCase()}`}>
              {coverage}
            </p>
          ) : null}
          {clips.length > 0 ? (
            <div className="space-y-3">
              {clips.map((clip, index) => (
                <ClipPlayer key={clip.recordingId} sampleId={detail.id} clip={clip} index={index} slot={slot} />
              ))}
            </div>
          ) : (
            <p className="text-sm text-admin-fg-muted">No audio clips.</p>
          )}
        </SettingsSection>

        <SettingsSection title={`Card ${slot} transcript`} description="As the AI grader reads it: connection-check chatter at the start is left out.">
          <ol className="space-y-2 text-sm" data-testid={`calibration-mock-transcript-${slot.toLowerCase()}`}>
            {transcript.map((line, index) => (
              <li key={index} className="grid grid-cols-[3.5rem_5.5rem_1fr] gap-2">
                <span className="tabular-nums text-admin-fg-muted">{formatClock(line.startMs)}</span>
                <span className="font-medium text-admin-fg-strong">{line.speaker.toLowerCase() === 'candidate' ? 'Candidate' : 'Patient'}</span>
                <span>{line.text}</span>
              </li>
            ))}
            {transcript.length === 0 ? <li className="text-admin-fg-muted">No transcript text.</li> : null}
          </ol>
        </SettingsSection>
      </>
    );
  };

  if (!detail) {
    return (
      <AdminSettingsLayout title="Mark a Full Mock" breadcrumbs={breadcrumbs} eyebrow="Speaking" icon={<Scale className="h-5 w-5" />}>
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : <Skeleton className="h-64 w-full" />}
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title={`Full Mock: ${detail.cardA.title || 'Card A'} + ${detail.cardB.title || 'Card B'}`}
      description="One test, one result: mark the whole two-card performance the way an OET assessor would — the nine criteria once, then your own overall result /500."
      breadcrumbs={breadcrumbs}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
      actions={detail.status === 'labelled' ? <Badge variant="success">Marked</Badge> : detail.status === 'excluded' ? <Badge variant="muted">Excluded</Badge> : <Badge variant="warning">To mark</Badge>}
    >
      <InlineAlert variant="info">
        <span className="inline-flex items-center gap-2">
          <EyeOff className="h-4 w-4" aria-hidden="true" />
          You are marking blind. The AI&apos;s score for this Full Mock is never shown on this page.
        </span>
      </InlineAlert>
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {detail.status === 'excluded' ? (
        <InlineAlert variant="warning">
          Excluded: {detail.excludedReason || 'no reason recorded'}. Saving marks below makes it usable again.
        </InlineAlert>
      ) : null}
      {!detail.hasAudio ? (
        <InlineAlert variant="warning">
          At least one card has no stored audio, so the combined grader&apos;s Intelligibility cannot be judged from real
          audio on this test — mark it anyway; the report records where Intelligibility came from.
        </InlineAlert>
      ) : null}

      {cardSection('A')}
      {cardSection('B')}

      <SettingsSection
        title="Your marks for the WHOLE test"
        description="One set of marks across both role-plays: linguistic criteria 0 to 6, clinical communication criteria 0 to 3."
      >
        <div className="space-y-4">
          {detail.criteria.map((criterion) => (
            <fieldset key={criterion.code} className="space-y-1.5" data-testid={`criterion-${criterion.code}`}>
              <legend className="text-sm font-medium text-admin-fg-strong">
                {criterion.label} <span className="font-normal text-admin-fg-muted">(0-{criterion.max})</span>
              </legend>
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: criterion.max + 1 }, (_, value) => (
                  <label
                    key={value}
                    className={`inline-flex h-9 min-w-9 cursor-pointer items-center justify-center rounded-admin-md border px-3 text-sm tabular-nums focus-within:ring-2 focus-within:ring-admin-primary ${
                      scores[criterion.code] === value
                        ? 'border-admin-primary bg-admin-primary text-white'
                        : 'border-admin-border bg-admin-bg-surface text-admin-fg-default hover:border-admin-fg-muted'
                    }`}
                  >
                    <input
                      type="radio"
                      name={`criterion-${criterion.code}`}
                      value={value}
                      checked={scores[criterion.code] === value}
                      onChange={() => setScores((current) => ({ ...current, [criterion.code]: value }))}
                      className="sr-only"
                      aria-label={`${criterion.label} ${value}`}
                    />
                    {value}
                  </label>
                ))}
              </div>
            </fieldset>
          ))}

          <p className="text-xs text-admin-fg-muted" data-testid="criteria-total">
            Criteria total: {rawTotal} / {rawMax}
          </p>

          <div className="space-y-1.5">
            <label htmlFor="calibration-mock-overall" className="text-sm font-medium text-admin-fg-strong">
              Your overall result for the whole test (0-500, in steps of 10)
            </label>
            <div className="flex items-center gap-3">
              <select
                id="calibration-mock-overall"
                value={overall ?? ''}
                onChange={(event) => setOverall(event.target.value === '' ? null : Number(event.target.value))}
                className="rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default"
              >
                <option value="">Choose a result</option>
                {OVERALL_OPTIONS.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
              {overall !== null ? (
                <span className="text-sm font-semibold text-admin-fg-strong" data-testid="overall-grade">
                  Grade {oetReportedGradeFromScaled(overall)}
                </span>
              ) : null}
            </div>
          </div>

          <Textarea
            label="Notes (optional)"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            rows={3}
            maxLength={2000}
          />

          <div className="flex flex-wrap items-center gap-3">
            <Button variant="primary" onClick={() => void save()} disabled={!complete || saving} loading={saving}>
              {detail.status === 'labelled' ? 'Update my marks' : 'Save my marks'}
            </Button>
            <Button asChild variant="outline">
              <Link href="/admin/speaking/grader-calibration">Back to the list</Link>
            </Button>
          </div>

          {saved ? (
            <InlineAlert variant="success">
              Marks saved.{' '}
              {nextId ? (
                <Link className="font-semibold underline" href={`/admin/speaking/grader-calibration/mock/${encodeURIComponent(nextId)}`}>
                  Mark the next Full Mock
                </Link>
              ) : (
                'No other Full Mock is waiting to be marked.'
              )}
            </InlineAlert>
          ) : null}
        </div>
      </SettingsSection>

      <SettingsSection title="Cannot be used?" description="No speech, the wrong card or broken audio: exclude it instead of guessing. It stays on record but is never used.">
        <div className="space-y-3">
          <Textarea
            label="Why can't it be used?"
            value={excludeReason}
            onChange={(event) => setExcludeReason(event.target.value)}
            rows={2}
            maxLength={500}
          />
          <Button variant="outline" onClick={() => void exclude()} disabled={saving || excludeReason.trim().length === 0}>
            Exclude this Full Mock
          </Button>
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
