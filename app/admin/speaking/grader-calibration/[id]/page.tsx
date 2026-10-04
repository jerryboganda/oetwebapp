'use client';

/**
 * Admin · Speaking · Grader calibration · mark one performance (owner spec 4 Oct 2026).
 *
 * Dr Hesham marks a real role-play against the nine OET criteria and gives his own overall result,
 * hearing the audio and reading the same transcript the AI grader reads. The AI's score is never
 * on this page, so the number being validated cannot anchor his marks.
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
  adminExcludeGraderCalibrationSample,
  adminGetGraderCalibration,
  adminGetGraderCalibrationSample,
  adminLabelGraderCalibrationSample,
  graderCalibrationAudioPath,
  type GraderCalibrationAudioClip,
  type GraderCalibrationSampleDetail,
} from '@/lib/api/speaking-grader-calibration';

const OVERALL_OPTIONS = Array.from({ length: 51 }, (_, index) => index * 10);

function formatClock(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

/** One clip of the performance, fetched with the admin's own authorisation when the page opens. */
function ClipPlayer({ sampleId, clip, index }: { sampleId: string; clip: GraderCalibrationAudioClip; index: number }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    fetchAuthorizedObjectUrl(graderCalibrationAudioPath(sampleId, clip.recordingId))
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
        Clip {index + 1} · {formatClock(clip.durationSeconds * 1000)}
      </p>
      {failed ? (
        <p className="text-xs text-admin-danger" role="alert">This clip could not be loaded.</p>
      ) : url ? (
        <audio controls src={url} className="w-full" aria-label={`Audio clip ${index + 1}`} />
      ) : (
        <Skeleton className="h-10 w-full" />
      )}
    </div>
  );
}

export default function SpeakingGraderCalibrationSamplePage() {
  const params = useParams<{ id: string }>();
  const sampleId = params?.id ?? '';

  const [detail, setDetail] = useState<GraderCalibrationSampleDetail | null>(null);
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
      const next = await adminGetGraderCalibrationSample(sampleId);
      setDetail(next);
      setScores(next.label?.scores ?? {});
      setOverall(next.label?.overallScaled ?? null);
      setNotes(next.label?.notes ?? '');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load this performance.');
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
      const overview = await adminGetGraderCalibration();
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
      await adminLabelGraderCalibrationSample(detail.id, { scores, overallScaled: overall, notes });
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
    if (!window.confirm('Exclude this performance? It will not be used for calibration.')) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await adminExcludeGraderCalibrationSample(detail.id, excludeReason);
      setExcludeReason('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not exclude this performance.');
    } finally {
      setSaving(false);
    }
  };

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Speaking', href: '/admin/speaking' },
    { label: 'Grader calibration', href: '/admin/speaking/grader-calibration' },
    { label: 'Mark a performance' },
  ];

  if (!detail) {
    return (
      <AdminSettingsLayout title="Mark a performance" breadcrumbs={breadcrumbs} eyebrow="Speaking" icon={<Scale className="h-5 w-5" />}>
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : <Skeleton className="h-64 w-full" />}
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title={detail.card.title || 'Mark a performance'}
      description="Mark it the way an OET assessor would: the nine criteria, then your own overall result."
      breadcrumbs={breadcrumbs}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
      actions={detail.status === 'labelled' ? <Badge variant="success">Marked</Badge> : detail.status === 'excluded' ? <Badge variant="muted">Excluded</Badge> : <Badge variant="warning">To mark</Badge>}
    >
      <InlineAlert variant="info">
        <span className="inline-flex items-center gap-2">
          <EyeOff className="h-4 w-4" aria-hidden="true" />
          You are marking blind. The AI&apos;s score for this performance is never shown on this page.
        </span>
      </InlineAlert>
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {detail.status === 'excluded' ? (
        <InlineAlert variant="warning">
          Excluded: {detail.excludedReason || 'no reason recorded'}. Saving marks below makes it usable again.
        </InlineAlert>
      ) : null}

      <SettingsSection title="The role-play card" description={`${detail.card.setting} · ${detail.card.candidateRole} with ${detail.card.interlocutorRole}`}>
        <div className="space-y-3 text-sm text-admin-fg-default">
          <p className="whitespace-pre-line">{detail.card.background}</p>
          {detail.card.tasks.length > 0 ? (
            <ol className="list-decimal space-y-1 pl-5">
              {detail.card.tasks.map((task, index) => (
                <li key={index} className="whitespace-pre-line">{task}</li>
              ))}
            </ol>
          ) : null}
        </div>
      </SettingsSection>

      <SettingsSection
        title="Audio"
        description={detail.hasAudio ? 'Listen first: intelligibility and fluency are judged from the audio.' : 'No audio was kept for this performance, so Intelligibility is judged from the transcript only.'}
      >
        {detail.clips.length > 0 ? (
          <div className="space-y-3">
            {detail.clips.map((clip, index) => (
              <ClipPlayer key={clip.recordingId} sampleId={detail.id} clip={clip} index={index} />
            ))}
          </div>
        ) : (
          <p className="text-sm text-admin-fg-muted">No audio clips.</p>
        )}
      </SettingsSection>

      <SettingsSection title="Transcript" description="As the AI grader reads it: connection-check chatter at the start is left out.">
        <ol className="space-y-2 text-sm" data-testid="calibration-transcript">
          {detail.transcript.map((line, index) => (
            <li key={index} className="grid grid-cols-[3.5rem_5.5rem_1fr] gap-2">
              <span className="tabular-nums text-admin-fg-muted">{formatClock(line.startMs)}</span>
              <span className="font-medium text-admin-fg-strong">{line.speaker.toLowerCase() === 'candidate' ? 'Candidate' : 'Patient'}</span>
              <span>{line.text}</span>
            </li>
          ))}
          {detail.transcript.length === 0 ? <li className="text-admin-fg-muted">No transcript text.</li> : null}
        </ol>
      </SettingsSection>

      <SettingsSection title="Your marks" description="Linguistic criteria are marked 0 to 6, clinical communication criteria 0 to 3.">
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
            <label htmlFor="calibration-overall" className="text-sm font-medium text-admin-fg-strong">
              Your overall result (0-500, in steps of 10)
            </label>
            <div className="flex items-center gap-3">
              <select
                id="calibration-overall"
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
                <Link className="font-semibold underline" href={`/admin/speaking/grader-calibration/${encodeURIComponent(nextId)}`}>
                  Mark the next performance
                </Link>
              ) : (
                'No other performance is waiting to be marked.'
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
            Exclude this performance
          </Button>
        </div>
      </SettingsSection>
    </AdminSettingsLayout>
  );
}
