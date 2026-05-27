'use client';

/**
 * Audio player + speaker-labelled transcript + timestamped tutor comments.
 *
 * - Top: HTML5 audio player (controlled).
 * - Below: transcript segments. Clicking a segment seeks the audio.
 * - Inline comment badges per segment range — click to expand.
 * - Slide-out comment composer (when not readOnly): tutor picks segment range +
 *   criterion + severity + body, then submits via the parent's `onAddComment`.
 *
 * Pure component: parent owns the comments array. We never re-fetch.
 */

import { useEffect, useMemo, useRef, useState, type ChangeEvent } from 'react';
import { MessageSquarePlus, Pause, Play, X } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Textarea } from '@/components/ui/form-controls';
import {
  CRITERION_LABEL,
  type SpeakingCriterionCode,
  type TimestampedComment,
  type TimestampedCommentInput,
} from '@/lib/api/speaking-assessments';
import { cn } from '@/lib/utils';

export interface TranscriptSegment {
  segmentId?: string;
  speaker: 'learner' | 'interlocutor' | 'system' | string;
  startMs: number;
  endMs: number;
  text: string;
}

export interface TranscriptPayload {
  segments: TranscriptSegment[];
}

export interface TranscriptPlayerWithCommentsProps {
  recordingUrl?: string | null;
  transcript: TranscriptPayload;
  comments: TimestampedComment[];
  onAddComment?: (input: TimestampedCommentInput) => Promise<void> | void;
  readOnly?: boolean;
}

const CRITERION_CODES: SpeakingCriterionCode[] = [
  'intelligibility',
  'fluency',
  'appropriateness',
  'grammarExpression',
  'relationshipBuilding',
  'patientPerspective',
  'structure',
  'informationGathering',
  'informationGiving',
];

const SEVERITY_OPTIONS: Array<{ value: 'note' | 'minor' | 'major'; label: string; variant: 'muted' | 'warning' | 'danger' }> = [
  { value: 'note', label: 'Note', variant: 'muted' },
  { value: 'minor', label: 'Minor', variant: 'warning' },
  { value: 'major', label: 'Major', variant: 'danger' },
];

function formatTime(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

function commentsForSegment(comments: TimestampedComment[], seg: TranscriptSegment): TimestampedComment[] {
  return comments.filter((c) => {
    // Comment overlaps the segment range
    return c.segmentEndMs >= seg.startMs && c.segmentStartMs <= seg.endMs;
  });
}

export function TranscriptPlayerWithComments({
  recordingUrl,
  transcript,
  comments,
  onAddComment,
  readOnly,
}: TranscriptPlayerWithCommentsProps) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentMs, setCurrentMs] = useState(0);
  const [expandedSegmentIndex, setExpandedSegmentIndex] = useState<number | null>(null);

  // Composer state
  const [composerOpen, setComposerOpen] = useState(false);
  const [composerSegments, setComposerSegments] = useState<{ startIdx: number; endIdx: number } | null>(null);
  const [composerCriterion, setComposerCriterion] = useState<SpeakingCriterionCode | ''>('');
  const [composerSeverity, setComposerSeverity] = useState<'note' | 'minor' | 'major'>('note');
  const [composerBody, setComposerBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const segments = useMemo(() => transcript.segments ?? [], [transcript.segments]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;
    const onTime = () => setCurrentMs(audio.currentTime * 1000);
    const onPlay = () => setIsPlaying(true);
    const onPause = () => setIsPlaying(false);
    audio.addEventListener('timeupdate', onTime);
    audio.addEventListener('play', onPlay);
    audio.addEventListener('pause', onPause);
    return () => {
      audio.removeEventListener('timeupdate', onTime);
      audio.removeEventListener('play', onPlay);
      audio.removeEventListener('pause', onPause);
    };
  }, []);

  const seek = (ms: number) => {
    const audio = audioRef.current;
    if (!audio) return;
    audio.currentTime = ms / 1000;
    setCurrentMs(ms);
  };

  const togglePlay = () => {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      void audio.play();
    } else {
      audio.pause();
    }
  };

  const activeIndex = useMemo(() => {
    for (let i = 0; i < segments.length; i++) {
      const s = segments[i];
      if (currentMs >= s.startMs && currentMs <= s.endMs) return i;
    }
    return -1;
  }, [currentMs, segments]);

  const openComposer = (segmentIndex: number) => {
    if (readOnly || !onAddComment) return;
    setComposerSegments({ startIdx: segmentIndex, endIdx: segmentIndex });
    setComposerCriterion('');
    setComposerSeverity('note');
    setComposerBody('');
    setError(null);
    setComposerOpen(true);
  };

  const submitComposer = async () => {
    if (!onAddComment || !composerSegments) return;
    if (!composerBody.trim()) {
      setError('Please enter a comment before saving.');
      return;
    }
    const startSeg = segments[composerSegments.startIdx];
    const endSeg = segments[composerSegments.endIdx];
    if (!startSeg || !endSeg) {
      setError('Selected segment is invalid.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onAddComment({
        transcriptSegmentIndex: composerSegments.startIdx,
        startMs: startSeg.startMs,
        endMs: endSeg.endMs,
        criterionCode: composerCriterion || undefined,
        severity: composerSeverity,
        bodyMarkdown: composerBody.trim(),
      });
      setComposerOpen(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save comment.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Card padding="md" className="flex flex-col gap-4" data-testid="transcript-player-with-comments">
      {/* Audio controls */}
      <div className="flex items-center gap-3 rounded-2xl border border-border bg-background-light p-3">
        <button
          type="button"
          onClick={togglePlay}
          className="inline-flex h-10 w-10 items-center justify-center rounded-full bg-primary text-white shadow-sm transition-transform active:scale-95"
          aria-label={isPlaying ? 'Pause recording' : 'Play recording'}
          disabled={!recordingUrl}
        >
          {isPlaying ? <Pause className="h-4 w-4" aria-hidden /> : <Play className="h-4 w-4 translate-x-0.5" aria-hidden />}
        </button>
        <div className="flex flex-1 flex-col">
          <span className="text-xs font-semibold text-muted">Recording</span>
          <span className="text-sm font-bold tabular-nums text-navy">{formatTime(currentMs)}</span>
        </div>
        {recordingUrl ? (
          <audio ref={audioRef} src={recordingUrl} preload="metadata" controls className="hidden" />
        ) : (
          <span className="text-xs italic text-muted">Recording unavailable</span>
        )}
      </div>

      {/* Transcript */}
      <div className="flex flex-col gap-2">
        {segments.length === 0 && (
          <p className="rounded-xl border border-dashed border-border bg-background-light/40 p-4 text-center text-sm text-muted">
            Transcript will appear here once processing is complete.
          </p>
        )}
        {segments.map((seg, i) => {
          const segComments = commentsForSegment(comments, seg);
          const isActive = i === activeIndex;
          const expanded = expandedSegmentIndex === i;
          return (
            <div
              key={seg.segmentId ?? `${seg.startMs}-${i}`}
              className={cn(
                'group rounded-xl border p-3 transition-colors',
                isActive ? 'border-primary/60 bg-primary/5' : 'border-border bg-surface',
              )}
            >
              <div className="flex items-baseline gap-2">
                <button
                  type="button"
                  onClick={() => seek(seg.startMs)}
                  className="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-background-light px-2.5 py-0.5 text-xs font-bold text-primary hover:bg-primary/10"
                  aria-label={`Seek to ${formatTime(seg.startMs)} (${seg.speaker})`}
                >
                  <span className="tabular-nums">[{formatTime(seg.startMs)}]</span>
                  <span className="capitalize">{seg.speaker}</span>
                </button>
                <p className="flex-1 text-sm leading-relaxed text-navy">{seg.text}</p>
                <div className="flex shrink-0 items-center gap-1">
                  {segComments.length > 0 && (
                    <button
                      type="button"
                      onClick={() => setExpandedSegmentIndex((v) => (v === i ? null : i))}
                      className="inline-flex items-center gap-1 rounded-full bg-info/10 px-2.5 py-0.5 text-xs font-bold text-info hover:bg-info/20"
                      aria-label={`${segComments.length} comment${segComments.length === 1 ? '' : 's'}; ${expanded ? 'collapse' : 'expand'}`}
                      aria-expanded={expanded}
                    >
                      {segComments.length} comment{segComments.length === 1 ? '' : 's'}
                    </button>
                  )}
                  {!readOnly && onAddComment && (
                    <button
                      type="button"
                      onClick={() => openComposer(i)}
                      className="inline-flex items-center gap-1 rounded-full border border-border bg-surface px-2.5 py-0.5 text-xs font-bold text-navy opacity-0 transition-opacity hover:bg-background-light group-hover:opacity-100"
                      aria-label={`Add comment on segment at ${formatTime(seg.startMs)}`}
                    >
                      <MessageSquarePlus className="h-3 w-3" aria-hidden />
                      Comment
                    </button>
                  )}
                </div>
              </div>

              {expanded && segComments.length > 0 && (
                <ul className="mt-2 space-y-1.5 border-l-2 border-info/40 pl-3">
                  {segComments.map((c) => {
                    const severity = SEVERITY_OPTIONS.find((opt) => opt.value === c.severity);
                    return (
                      <li key={c.commentId} className="flex flex-col gap-0.5 text-xs">
                        <span className="flex flex-wrap items-center gap-1.5">
                          {c.criterion && <Badge variant="info">{CRITERION_LABEL[c.criterion as SpeakingCriterionCode]}</Badge>}
                          {severity && <Badge variant={severity.variant}>{severity.label}</Badge>}
                          {c.tutorName && <span className="font-semibold text-navy">{c.tutorName}</span>}
                          <span className="text-muted">
                            {formatTime(c.segmentStartMs)}–{formatTime(c.segmentEndMs)}
                          </span>
                        </span>
                        <p className="text-sm leading-relaxed text-navy">{c.body}</p>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          );
        })}
      </div>

      {/* Composer slide-out */}
      {composerOpen && composerSegments && (
        <div
          role="dialog"
          aria-label="Add timestamped comment"
          className="fixed inset-y-0 right-0 z-40 flex w-full max-w-md flex-col bg-surface shadow-xl ring-1 ring-border md:rounded-l-2xl"
        >
          <div className="flex items-start justify-between border-b border-border p-4">
            <div>
              <h3 className="text-base font-bold text-navy">New comment</h3>
              <p className="text-xs text-muted">
                Segment {formatTime(segments[composerSegments.startIdx]?.startMs ?? 0)}–
                {formatTime(segments[composerSegments.endIdx]?.endMs ?? 0)}
              </p>
            </div>
            <button
              type="button"
              onClick={() => setComposerOpen(false)}
              aria-label="Close composer"
              className="rounded-full p-1 text-muted hover:bg-background-light hover:text-navy"
            >
              <X className="h-4 w-4" aria-hidden />
            </button>
          </div>

          <div className="flex flex-1 flex-col gap-3 overflow-y-auto p-4">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="composer-criterion" className="text-sm font-semibold text-navy">
                Criterion (optional)
              </label>
              <select
                id="composer-criterion"
                value={composerCriterion}
                onChange={(e: ChangeEvent<HTMLSelectElement>) => setComposerCriterion(e.target.value as SpeakingCriterionCode | '')}
                className="rounded-2xl border border-border bg-background-light px-3 py-2.5 text-sm text-navy focus:border-primary focus:outline-none focus:ring-4 focus:ring-primary/15"
              >
                <option value="">No criterion</option>
                {CRITERION_CODES.map((code) => (
                  <option key={code} value={code}>
                    {CRITERION_LABEL[code]}
                  </option>
                ))}
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-semibold text-navy">Severity</span>
              <div className="flex gap-2">
                {SEVERITY_OPTIONS.map((opt) => (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={() => setComposerSeverity(opt.value)}
                    className={cn(
                      'inline-flex flex-1 items-center justify-center rounded-full border px-3 py-2 text-xs font-bold transition-colors',
                      composerSeverity === opt.value
                        ? 'border-primary bg-primary text-white'
                        : 'border-border bg-background-light text-navy hover:border-primary/40',
                    )}
                    aria-pressed={composerSeverity === opt.value}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
            </div>

            <Textarea
              label="Comment"
              value={composerBody}
              onChange={(e) => setComposerBody(e.target.value)}
              rows={5}
              hint="Visible to the learner. Be specific, kind, and actionable."
              error={error ?? undefined}
            />
          </div>

          <div className="flex items-center justify-end gap-2 border-t border-border p-4">
            <Button variant="ghost" onClick={() => setComposerOpen(false)}>
              Cancel
            </Button>
            <Button onClick={() => void submitComposer()} loading={submitting} disabled={submitting}>
              Save comment
            </Button>
          </div>
        </div>
      )}
    </Card>
  );
}

export default TranscriptPlayerWithComments;
