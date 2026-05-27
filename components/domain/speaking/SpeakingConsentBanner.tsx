'use client';

/**
 * Phase 7 (C.3) of the OET Speaking module plan.
 *
 * Modal banner shown before any audio/video capture begins. The learner
 * MUST explicitly accept the relevant consent before they can record.
 * Captures the current consent version (`recording.v1` or
 * `live_video_with_tutor.v1` for live tutor rooms) by posting to
 * `/v1/speaking/consents` via `lib/api/speaking-compliance.ts`.
 *
 * Returns `null` after the learner accepts, freeing the wrapping page to
 * mount its recorder. Failure to post the consent keeps the modal open
 * and surfaces an error so the recorder never starts without a record.
 */

import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { AlertTriangle, Loader2, ShieldCheck } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { apiClient } from '@/lib/api';
import {
  recordSpeakingConsent,
  type SpeakingConsentType,
} from '@/lib/api/speaking-compliance';

const FALLBACK_DISCLAIMER =
  'Practice estimate only. This is not an official OET score or result.';

const FALLBACK_RECORDING_BODY =
  'By recording you agree that your audio will be processed by our AI evaluator and may be reviewed by a human tutor. Recordings are stored securely and deleted after the configured retention window.';

const FALLBACK_LIVE_VIDEO_BODY =
  'By joining a live tutor session you agree to share your audio and video with the tutor in real time. The session is recorded for review and stored securely until the retention period elapses.';

interface ComplianceCopy {
  consentText?: unknown;
  scoreDisclaimer?: unknown;
}

export type SpeakingSessionMode = 'ai' | 'live_tutor';

export interface SpeakingConsentBannerProps {
  sessionMode: SpeakingSessionMode;
  /** Invoked once the consent row has been written successfully. */
  onAccepted: () => void;
  /** Optional explicit consent version override (testing). */
  consentVersionOverride?: string;
  /** Test hook — replace the API call with an injected promise. */
  postConsent?: (input: {
    consentType: SpeakingConsentType;
    consentVersion?: string;
  }) => Promise<unknown>;
}

function resolveConsentType(mode: SpeakingSessionMode): SpeakingConsentType {
  return mode === 'live_tutor' ? 'live_video_with_tutor' : 'recording';
}

export function SpeakingConsentBanner({
  sessionMode,
  onAccepted,
  consentVersionOverride,
  postConsent,
}: SpeakingConsentBannerProps) {
  const consentType = useMemo(() => resolveConsentType(sessionMode), [sessionMode]);
  const titleId = useId();
  const descId = useId();
  const [accepting, setAccepting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState(false);
  const [serverDisclaimer, setServerDisclaimer] = useState<string>(FALLBACK_DISCLAIMER);
  const [serverConsentText, setServerConsentText] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .request<ComplianceCopy>('/v1/speaking/compliance')
      .then((data) => {
        if (cancelled) return;
        if (typeof data.scoreDisclaimer === 'string' && data.scoreDisclaimer.trim().length > 0) {
          setServerDisclaimer(data.scoreDisclaimer);
        }
        if (typeof data.consentText === 'string' && data.consentText.trim().length > 0) {
          setServerConsentText(data.consentText);
        }
      })
      .catch(() => {
        // Silent — fall through to the static copy below.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const bodyCopy =
    serverConsentText
      ?? (sessionMode === 'live_tutor' ? FALLBACK_LIVE_VIDEO_BODY : FALLBACK_RECORDING_BODY);

  const handleAccept = useCallback(async () => {
    setError(null);
    setAccepting(true);
    try {
      const post =
        postConsent ??
        ((input: { consentType: SpeakingConsentType; consentVersion?: string }) =>
          recordSpeakingConsent(input));
      if (sessionMode === 'live_tutor') {
        await post({ consentType: 'recording' });
        await post({ consentType: 'live_video_with_tutor' });
      } else {
        await post({
          consentType,
          consentVersion: consentVersionOverride,
        });
      }
      setAccepted(true);
      onAccepted();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not record consent. Please try again.';
      setError(msg);
    } finally {
      setAccepting(false);
    }
  }, [consentType, consentVersionOverride, onAccepted, postConsent, sessionMode]);

  if (accepted) {
    return null;
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      aria-describedby={descId}
      className="fixed inset-0 z-50 flex items-end justify-center bg-black/60 p-4 sm:items-center"
      data-testid="speaking-consent-banner"
    >
      <div className="w-full max-w-lg rounded-2xl bg-surface p-6 shadow-2xl">
        <div className="mb-4 flex items-start gap-3">
          <ShieldCheck className="mt-0.5 h-6 w-6 shrink-0 text-emerald-600" aria-hidden />
          <div>
            <h2 id={titleId} className="text-lg font-semibold text-foreground">
              {sessionMode === 'live_tutor'
                ? 'Consent to record live tutor session'
                : 'Consent to record this practice session'}
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              We need a one-time confirmation before recording starts.
            </p>
          </div>
        </div>

        <div id={descId} className="space-y-3 text-sm leading-relaxed text-foreground">
          <p>{bodyCopy}</p>
          <div className="flex items-start gap-2 rounded-lg border border-amber-300/60 bg-amber-50 p-3 text-amber-900">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <p className="text-xs">{serverDisclaimer}</p>
          </div>
        </div>

        {error ? (
          <p
            role="alert"
            className="mt-3 rounded-md border border-rose-300 bg-rose-50 p-2 text-xs text-rose-700"
          >
            {error}
          </p>
        ) : null}

        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            type="button"
            variant="primary"
            disabled={accepting}
            onClick={() => void handleAccept()}
            data-testid="speaking-consent-accept"
          >
            {accepting ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden />
                Saving consent…
              </>
            ) : (
              'I consent — start recording'
            )}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default SpeakingConsentBanner;
