'use client';

/**
 * OET Mocks V2 Wave 6 — Speaking audio-only live-room (learner side).
 *
 * Timed flow per spec:
 *   1. Pre-room       — recording-consent gate + browser mic check.
 *   2. Roleplay 1 prep    — 3 minutes, candidate-card panel only.
 *   3. Roleplay 1 speaking — 5 minutes, MediaRecorder uploads chunks.
 *   4. Roleplay 2 prep    — 3 minutes (skipped if booking has 1 roleplay).
 *   5. Roleplay 2 speaking — 5 minutes, same recording flow.
 *   6. Submit             — finalises chunks + transitions live-room to "completed".
 *
 * Privacy & integrity (mission-critical invariants):
 *   - Interlocutor identity is NEVER displayed; the backend booking projection
 *     for non-admins strips it (`interlocutorCardVisible: false`).
 *   - Zoom start URL / password are NEVER returned to learners.
 *   - Recording is gated on `consentToRecording === true`; both the chunked
 *     upload and finalize endpoints reject otherwise.
 *   - Audio bytes flow through the backend's `IFileStorage` pipeline only —
 *     no direct File.* / Path.* on the server. SHA-256 dedup per chunk.
 *   - Live-room state transitions are server-authoritative (audit-trailed).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import {
  ArrowLeft,
  CalendarClock,
  CheckCircle2,
  CircleDot,
  Mic,
  ShieldCheck,
  Square,
  Upload,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MicCheckPanel } from '@/components/domain/mic-check-panel';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Timer } from '@/components/ui/timer';
import {
  appendMockBookingRecordingChunk,
  fetchMockBookingList,
  finalizeMockBookingRecording,
  transitionMockBookingLiveRoom,
} from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

const PREP_SECONDS = 3 * 60;
const SPEAK_SECONDS = 5 * 60;
const CHUNK_TIMESLICE_MS = 7000; // ~7s per MediaRecorder chunk
const ROLEPLAY_COUNT = 2;

type Phase =
  | 'pre'
  | 'rp1-prep'
  | 'rp1-speak'
  | 'rp2-prep'
  | 'rp2-speak'
  | 'submitting'
  | 'done';

function pickMimeType(): string | undefined {
  if (typeof MediaRecorder === 'undefined' || typeof MediaRecorder.isTypeSupported !== 'function') {
    return undefined;
  }
  return [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/mp4',
    'audio/ogg',
  ].find((m) => MediaRecorder.isTypeSupported(m));
}

export default function SpeakingLiveRoomPage() {
  const params = useParams<{ bookingId: string }>();
  const router = useRouter();
  const bookingId = params?.bookingId ?? '';

  const [booking, setBooking] = useState<MockBooking | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const [phase, setPhase] = useState<Phase>('pre');
  const [consent, setConsent] = useState(false);
  const [micPassed, setMicPassed] = useState(false);

  const [chunkCount, setChunkCount] = useState(0);
  const [uploading, setUploading] = useState(false);

  // MediaRecorder + stream lifecycle.
  const streamRef = useRef<MediaStream | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const partRef = useRef(0);
  const inflightRef = useRef<Promise<void>>(Promise.resolve());
  const startMsRef = useRef<number | null>(null);
  const totalDurationMsRef = useRef(0);

  // ── Load booking ─────────────────────────────────────────────────────────
  useEffect(() => {
    if (!bookingId) return;
    analytics.track('content_view', { page: 'mock-speaking-room', bookingId });
    fetchMockBookingList()
      .then((response) => {
        const matched = response.items.find(
          (b) => b.bookingId === bookingId || b.id === bookingId,
        );
        if (!matched) {
          setError('Booking not found or no longer accessible from this account.');
          return;
        }
        setBooking(matched);
        setConsent(Boolean(matched.consentToRecording));
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this booking.'))
      .finally(() => setLoading(false));
  }, [bookingId]);

  // ── Cleanup on unmount ───────────────────────────────────────────────────
  useEffect(() => () => {
    try {
      if (recorderRef.current && recorderRef.current.state !== 'inactive') {
        recorderRef.current.stop();
      }
    } catch {
      /* noop */
    }
    recorderRef.current = null;
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
  }, []);

  // ── Recorder lifecycle ───────────────────────────────────────────────────
  const startRecorder = useCallback(async () => {
    if (!booking) return;
    if (!booking.consentToRecording) {
      setError('Recording consent has not been granted on this booking — recording cannot start.');
      return;
    }
    setError(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      streamRef.current = stream;
      const mime = pickMimeType();
      const recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
      recorderRef.current = recorder;
      startMsRef.current = Date.now();

      recorder.ondataavailable = (event) => {
        if (!event.data || event.data.size === 0) return;
        const part = partRef.current++;
        const blob = event.data;
        // Serialise uploads to preserve order on the server's manifest.
        inflightRef.current = inflightRef.current
          .then(async () => {
            setUploading(true);
            try {
              const ack = await appendMockBookingRecordingChunk(bookingId, part, blob);
              setChunkCount(ack.chunkCount);
            } catch (err) {
              setError(err instanceof Error ? err.message : 'Chunk upload failed — recording will keep trying.');
            } finally {
              setUploading(false);
            }
          })
          .catch(() => {
            setUploading(false);
          });
      };

      recorder.start(CHUNK_TIMESLICE_MS);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Microphone access failed. Please grant access and try again.');
      setPhase('pre');
    }
  }, [booking, bookingId]);

  const stopRecorder = useCallback(async () => {
    const recorder = recorderRef.current;
    if (recorder && recorder.state !== 'inactive') {
      await new Promise<void>((resolve) => {
        recorder.addEventListener('stop', () => resolve(), { once: true });
        recorder.stop();
      });
    }
    if (startMsRef.current !== null) {
      totalDurationMsRef.current += Date.now() - startMsRef.current;
      startMsRef.current = null;
    }
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    recorderRef.current = null;
    // Wait for any in-flight uploads to drain so the manifest is complete.
    await inflightRef.current.catch(() => undefined);
  }, []);

  // ── Phase transitions ────────────────────────────────────────────────────
  const enterPhase = useCallback(
    async (target: Phase) => {
      setError(null);
      // Ensure the previous speaking phase has its recorder stopped.
      const wasSpeaking = phase === 'rp1-speak' || phase === 'rp2-speak';
      if (wasSpeaking && (target === 'rp2-prep' || target === 'submitting' || target === 'done')) {
        await stopRecorder();
      }
      setPhase(target);
      if (target === 'rp1-speak' || target === 'rp2-speak') {
        await startRecorder();
      }
    },
    [phase, startRecorder, stopRecorder],
  );

  const handleStart = useCallback(async () => {
    if (!booking) return;
    if (!consent) {
      setError('You must accept the recording consent to enter the live room.');
      return;
    }
    if (!micPassed) {
      setError('Please complete the microphone check before starting.');
      return;
    }
    try {
      const updated = await transitionMockBookingLiveRoom(
        booking.bookingId ?? booking.id,
        'in_progress',
      );
      setBooking(updated);
      analytics.track('mock_started', { bookingId, phase: 'rp1-prep' });
      await enterPhase('rp1-prep');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start the live room.');
    }
  }, [booking, bookingId, consent, micPassed, enterPhase]);

  const handleSubmit = useCallback(async () => {
    if (!booking) return;
    setPhase('submitting');
    setError(null);
    try {
      await stopRecorder();
      await finalizeMockBookingRecording(
        booking.bookingId ?? booking.id,
        Math.max(1, Math.round(totalDurationMsRef.current)),
      );
      const updated = await transitionMockBookingLiveRoom(
        booking.bookingId ?? booking.id,
        'completed',
      );
      setBooking(updated);
      setInfo('Recording submitted. Redirecting…');
      analytics.track('mock_completed', { bookingId });
      setPhase('done');
      const target = updated.mockAttemptId
        ? `/mocks/report/${updated.mockAttemptId}`
        : '/mocks';
      setTimeout(() => router.push(target), 1500);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Submission failed. Please try again.');
      setPhase('rp2-speak');
    }
  }, [booking, bookingId, router, stopRecorder]);

  // ── Derived display state ────────────────────────────────────────────────
  const scheduledStart = useMemo(() => {
    if (!booking) return null;
    try {
      return new Date(booking.scheduledStartAt).toLocaleString();
    } catch {
      return booking.scheduledStartAt;
    }
  }, [booking]);

  const interlocutorHidden = booking?.interlocutorCardVisible === false;

  return (
    <LearnerDashboardShell
      pageTitle="Speaking Live Room"
      subtitle="Audio-only role-play — timed flow with chunked recording."
      backHref="/mocks/bookings"
    >
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/mocks/bookings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to bookings
        </Button>

        {loading ? (
          <Skeleton className="h-72 rounded-3xl" />
        ) : error && !booking ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : booking ? (
          <>
            <LearnerPageHero
              eyebrow="Speaking · Live role-play"
              icon={Mic}
              accent="navy"
              title={booking.title ?? 'OET Speaking Mock'}
              description="Audio-only delivery with full OET timing: 3-minute prep, 5-minute role-play, twice. Your audio is captured client-side and uploaded in small chunks while you speak."
              highlights={[
                { icon: CalendarClock, label: 'Scheduled', value: scheduledStart ?? '—' },
                {
                  icon: ShieldCheck,
                  label: 'Recording consent',
                  value: booking.consentToRecording ? 'Granted' : 'Not granted',
                },
                {
                  icon: CheckCircle2,
                  label: 'Phase',
                  value: phaseLabel(phase),
                },
              ]}
            />

            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            {info ? <InlineAlert variant="success">{info}</InlineAlert> : null}

            <PhaseStepper phase={phase} />

            {phase === 'pre' ? (
              <PreRoom
                booking={booking}
                consent={consent}
                onConsentChange={setConsent}
                micPassed={micPassed}
                onMicComplete={() => setMicPassed(true)}
                onStart={handleStart}
                interlocutorHidden={interlocutorHidden}
              />
            ) : null}

            {phase === 'rp1-prep' ? (
              <PrepPanel
                roleplayIndex={1}
                seconds={PREP_SECONDS}
                onAdvance={() => { void enterPhase('rp1-speak'); }}
              />
            ) : null}
            {phase === 'rp1-speak' ? (
              <SpeakPanel
                roleplayIndex={1}
                seconds={SPEAK_SECONDS}
                chunkCount={chunkCount}
                uploading={uploading}
                onAdvance={() => {
                  if (ROLEPLAY_COUNT === 2) {
                    void enterPhase('rp2-prep');
                  } else {
                    void handleSubmit();
                  }
                }}
              />
            ) : null}

            {phase === 'rp2-prep' ? (
              <PrepPanel
                roleplayIndex={2}
                seconds={PREP_SECONDS}
                onAdvance={() => { void enterPhase('rp2-speak'); }}
              />
            ) : null}
            {phase === 'rp2-speak' ? (
              <SpeakPanel
                roleplayIndex={2}
                seconds={SPEAK_SECONDS}
                chunkCount={chunkCount}
                uploading={uploading}
                onAdvance={() => { void handleSubmit(); }}
                advanceLabel="Stop & submit"
                advanceIcon={Upload}
              />
            ) : null}

            {phase === 'submitting' ? (
              <Skeleton className="h-32 rounded-3xl" />
            ) : null}

            {phase === 'done' ? (
              <section className="rounded-3xl border border-border bg-surface p-6 text-sm leading-6 text-muted shadow-sm">
                Recording submitted. {info ?? 'Redirecting to your report…'}
              </section>
            ) : null}
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

// ─── Sub-components ────────────────────────────────────────────────────────

function phaseLabel(phase: Phase): string {
  switch (phase) {
    case 'pre':
      return 'Pre-room';
    case 'rp1-prep':
      return 'Role-play 1 — preparation';
    case 'rp1-speak':
      return 'Role-play 1 — speaking';
    case 'rp2-prep':
      return 'Role-play 2 — preparation';
    case 'rp2-speak':
      return 'Role-play 2 — speaking';
    case 'submitting':
      return 'Submitting';
    case 'done':
      return 'Complete';
  }
}

function PhaseStepper({ phase }: { phase: Phase }) {
  const steps: { key: Phase; label: string }[] = [
    { key: 'pre', label: 'Pre-room' },
    { key: 'rp1-prep', label: 'RP1 prep' },
    { key: 'rp1-speak', label: 'RP1 speak' },
    { key: 'rp2-prep', label: 'RP2 prep' },
    { key: 'rp2-speak', label: 'RP2 speak' },
    { key: 'done', label: 'Submit' },
  ];
  const activeIndex = steps.findIndex((s) => s.key === phase);
  return (
    <ol className="flex flex-wrap gap-2 text-xs">
      {steps.map((s, i) => {
        const passed = i < activeIndex || phase === 'done';
        const active = i === activeIndex;
        return (
          <li
            key={s.key}
            className={
              'rounded-full border px-3 py-1 font-bold ' +
              (active
                ? 'border-primary bg-primary/10 text-primary'
                : passed
                  ? 'border-success/40 bg-success/10 text-success'
                  : 'border-border bg-background-light text-muted')
            }
          >
            {i + 1}. {s.label}
          </li>
        );
      })}
    </ol>
  );
}

function PreRoom({
  booking,
  consent,
  onConsentChange,
  micPassed,
  onMicComplete,
  onStart,
  interlocutorHidden,
}: {
  booking: MockBooking;
  consent: boolean;
  onConsentChange: (v: boolean) => void;
  micPassed: boolean;
  onMicComplete: () => void;
  onStart: () => void;
  interlocutorHidden: boolean;
}) {
  const canStart = consent && micPassed && Boolean(booking.consentToRecording);
  return (
    <section className="space-y-4">
      <div className="rounded-3xl border border-border bg-surface p-6 shadow-sm">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="info" size="sm">Pre-room checks</Badge>
          {interlocutorHidden ? (
            <Badge variant="muted" size="sm">Interlocutor identity hidden by platform</Badge>
          ) : null}
        </div>
        <h3 className="mt-3 text-lg font-black text-foreground">Recording consent</h3>
        <p className="mt-1 text-sm leading-6 text-muted">
          Your role-play audio is captured client-side and streamed to OET-secured storage in small
          chunks. Audio is used only for marking and feedback. You can withdraw consent at any time
          before starting by leaving this page.
        </p>
        <label className="mt-3 flex items-start gap-3 text-sm text-foreground">
          <input
            type="checkbox"
            className="mt-1 h-4 w-4 rounded border-border"
            checked={consent}
            onChange={(e) => onConsentChange(e.target.checked)}
            disabled={!booking.consentToRecording}
          />
          <span>
            I consent to the audio capture for this Speaking mock.
            {!booking.consentToRecording ? (
              <span className="ml-1 text-warning">
                (Recording was not enabled on this booking — recording cannot start.)
              </span>
            ) : null}
          </span>
        </label>
      </div>

      {micPassed ? (
        <div className="rounded-3xl border border-success/30 bg-success/5 p-4 text-sm text-success">
          <CheckCircle2 className="mr-2 inline h-4 w-4" />
          Microphone check passed.
        </div>
      ) : (
        <div className="rounded-3xl border border-border bg-surface p-4 shadow-sm">
          <MicCheckPanel onComplete={onMicComplete} />
        </div>
      )}

      <div className="flex justify-end">
        <Button
          variant="primary"
          size="md"
          onClick={onStart}
          disabled={!canStart}
        >
          Start role-play
        </Button>
      </div>
    </section>
  );
}

function PrepPanel({
  roleplayIndex,
  seconds,
  onAdvance,
}: {
  roleplayIndex: number;
  seconds: number;
  onAdvance: () => void;
}) {
  return (
    <section className="rounded-3xl border border-border bg-surface p-6 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Badge variant="info" size="sm">Role-play {roleplayIndex} — preparation</Badge>
          <h3 className="mt-2 text-lg font-black text-foreground">3 minutes to read your card</h3>
          <p className="mt-1 text-sm leading-6 text-muted">
            Read the candidate card you have been given (printed or in chat). The interlocutor card
            is intentionally not shown to candidates. The recorder will start automatically when this
            timer ends, or click <strong>I&apos;m ready</strong> below.
          </p>
        </div>
        <Timer
          mode="countdown"
          initialSeconds={seconds}
          running
          onComplete={onAdvance}
          size="lg"
        />
      </div>
      <div className="mt-4 flex justify-end">
        <Button variant="secondary" onClick={onAdvance}>
          I&apos;m ready — start speaking
        </Button>
      </div>
    </section>
  );
}

function SpeakPanel({
  roleplayIndex,
  seconds,
  chunkCount,
  uploading,
  onAdvance,
  advanceLabel = 'Stop early',
  advanceIcon: AdvanceIcon = Square,
}: {
  roleplayIndex: number;
  seconds: number;
  chunkCount: number;
  uploading: boolean;
  onAdvance: () => void;
  advanceLabel?: string;
  advanceIcon?: typeof Square;
}) {
  return (
    <section className="rounded-3xl border border-primary/40 bg-primary/5 p-6 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Badge variant="danger" size="sm">
            <CircleDot className="mr-1 inline h-3 w-3 animate-pulse" />
            Recording role-play {roleplayIndex}
          </Badge>
          <h3 className="mt-2 text-lg font-black text-foreground">5 minutes to deliver</h3>
          <p className="mt-1 text-sm leading-6 text-muted">
            Speak naturally with your interlocutor. Audio is uploaded every ~7 seconds; if a chunk
            fails the next chunk will retry automatically.
          </p>
          <p className="mt-2 text-xs font-bold text-muted">
            Chunks uploaded: {chunkCount} {uploading ? '· uploading…' : ''}
          </p>
        </div>
        <Timer
          mode="countdown"
          initialSeconds={seconds}
          running
          onComplete={onAdvance}
          size="lg"
        />
      </div>
      <div className="mt-4 flex justify-end">
        <Button variant="secondary" onClick={onAdvance} className="gap-2">
          <AdvanceIcon className="h-4 w-4" />
          {advanceLabel}
        </Button>
      </div>
    </section>
  );
}
