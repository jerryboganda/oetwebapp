'use client';

/**
 * 23 Sep 2026 owner flow (every Speaking mode): ONE Rules + consent step
 * BEFORE any timer runs. Replaces the RecordingConsentGate modal, the task
 * page's inline/finish-dialog consent checkboxes and the live-voice provider
 * disclosure checkbox. The caller's `onStart` records consent server-side and
 * begins preparation; nothing is timed while this screen is shown.
 */
import { useEffect, useId, useState } from 'react';
import { ClipboardList, Loader2, Play } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { apiClient } from '@/lib/api';

export const FREE_SPEAKING_SAMPLE_COPY = 'Free sample includes one full attempt. New attempts require Speaking credits.';

const FALLBACK_RECORDING_NOTICE =
  'Your voice is processed during the session to provide AI-powered speaking practice and feedback. Audio recordings are retained for a limited period and may be reviewed to support your feedback and improve the service.';

const AI_PROVIDER_DISCLOSURE =
  'For a live AI-patient role-play, short clips are captured from your microphone when speech is detected and stored for the stated retention period. The app does not make a full-session recording or directly record provider playback. Browser echo cancellation is enabled, but speaker or background audio may still be picked up by your microphone. Your microphone is also streamed in real time to the live voice provider, and your audio and transcript are sent to our AI speech-to-text and grading providers to assess your performance.';

interface ComplianceCopy {
  consentText?: unknown;
  speakingSimulationV11RetentionNotice?: unknown;
}

export interface SpeakingRulesConsentProps {
  /** Records consent and begins preparation. Throwing keeps the learner on this step with the error shown. */
  onStart: () => Promise<void> | void;
  /** Shows the free-sample allowance line. */
  freeSample?: boolean;
  /** Two-card full exam wording instead of one card. */
  exam?: boolean;
  startLabel?: string;
  className?: string;
}

export function SpeakingRulesConsent({
  onStart,
  freeSample = false,
  exam = false,
  startLabel = 'Start preparation',
  className,
}: SpeakingRulesConsentProps) {
  const checkboxId = useId();
  const [accepted, setAccepted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [consentText, setConsentText] = useState<string>(FALLBACK_RECORDING_NOTICE);
  const [retentionNotice, setRetentionNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .request<ComplianceCopy>('/v1/speaking/compliance')
      .then((copy) => {
        if (cancelled || !copy) return;
        if (typeof copy.consentText === 'string' && copy.consentText.trim()) setConsentText(copy.consentText);
        if (typeof copy.speakingSimulationV11RetentionNotice === 'string' && copy.speakingSimulationV11RetentionNotice.trim()) {
          setRetentionNotice(copy.speakingSimulationV11RetentionNotice);
        }
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);

  const start = async () => {
    if (!accepted || busy) return;
    setBusy(true);
    setError(null);
    try {
      await onStart();
    } catch (caught) {
      setError(caught instanceof Error && caught.message ? caught.message : 'Could not start. Please try again.');
      setBusy(false);
      return;
    }
    // Success: the caller navigates or swaps this step out; stay disabled so
    // a second tap cannot start twice.
  };

  return (
    <section
      aria-labelledby={`${checkboxId}-title`}
      className={className ?? 'space-y-4 rounded-2xl border border-border bg-surface p-4 sm:p-6'}
      data-testid="speaking-rules-consent"
    >
      <div className="flex items-start gap-3">
        <ClipboardList className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
        <div>
          <h2 id={`${checkboxId}-title`} className="text-lg font-bold text-navy">Before you start</h2>
          <p className="text-sm text-muted">Computer-based OET Speaking rules. No timer is running yet.</p>
        </div>
      </div>

      <ol className="list-decimal space-y-1.5 pl-5 text-sm leading-relaxed text-navy">
        {exam ? (
          <li>A short unscored introduction, then two role-play cards (Card A and Card B).</li>
        ) : null}
        <li>Read the role-play card carefully. It cannot be highlighted or annotated on screen.</li>
        <li>You have <strong>3 minutes to prepare</strong>.</li>
        <li>Then you have a <strong>5-minute role-play</strong> with the AI patient (interlocutor).</li>
        <li>Use <strong>one blank sheet of paper and a pen</strong> for notes. There are no on-screen notes.</li>
        <li>Your audio is recorded and graded by AI.</li>
      </ol>

      {freeSample ? (
        <p className="rounded-lg border border-primary/20 bg-primary/5 px-3 py-2 text-sm font-semibold text-primary" data-testid="speaking-free-sample-note">
          {FREE_SPEAKING_SAMPLE_COPY}
        </p>
      ) : null}

      <div className="space-y-2 rounded-lg border border-border bg-background-light p-3 text-xs leading-relaxed text-muted">
        <p>{consentText}</p>
        {retentionNotice ? <p>{retentionNotice}</p> : null}
        <p>{AI_PROVIDER_DISCLOSURE}</p>
      </div>

      <label htmlFor={checkboxId} className="flex cursor-pointer items-start gap-3 text-sm leading-relaxed text-navy">
        <input
          id={checkboxId}
          type="checkbox"
          checked={accepted}
          onChange={(event) => setAccepted(event.target.checked)}
          disabled={busy}
          className="mt-0.5 h-5 w-5 shrink-0 rounded border-border"
        />
        <span>I have read the rules and I consent to my audio being recorded, stored and processed by AI for assessment.</span>
      </label>

      {error ? (
        <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger-strong">
          {error}
        </p>
      ) : null}

      <Button type="button" fullWidth size="lg" onClick={() => void start()} disabled={!accepted || busy}>
        {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : <Play className="mr-2 h-4 w-4" aria-hidden />}
        {startLabel}
      </Button>
    </section>
  );
}

export default SpeakingRulesConsent;
