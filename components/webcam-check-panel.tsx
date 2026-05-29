'use client';

/**
 * Mocks V2 Phase 2 — webcam pre-flight panel.
 *
 * Mirrors the `<MicCheckPanel>` pattern in `components/domain/mic-check-panel.tsx`
 * but uses `useWebcamPreflight` to drive a video preview. Designed to be
 * mounted alongside the mic check on Speaking sections of `exam` /
 * `final_readiness` strictness mocks.
 *
 * Contract:
 *  - `storageKey` is opaque to this component; the parent owns its shape and
 *    typically uses `preflightStorageKey('camera', sessionId, sectionId)`.
 *  - `onPassed` fires exactly once per transition into `granted`.
 *  - `onFailed(reason)` fires on each transition into `denied` / `unavailable`.
 *  - `required` is informational — the parent enforces the gate itself.
 */

import { useEffect, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, Loader2, Video, VideoOff } from 'lucide-react';
import { motion } from 'motion/react';

import { useWebcamPreflight, type WebcamPreflightStatus } from '@/hooks/use-webcam-preflight';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { cn } from '@/lib/utils';

interface WebcamCheckPanelProps {
  /** sessionStorage key for the granted-flag. Owned by the parent. */
  storageKey: string;
  /** Called the first time the camera permission transitions to granted. */
  onPassed: () => void;
  /** Called when the camera is blocked or no device is present. */
  onFailed: (reason: 'denied' | 'unavailable' | 'browser_unsupported') => void;
  /** When true, copy emphasises that the check is mandatory. */
  required?: boolean;
  className?: string;
}

function statusReason(status: WebcamPreflightStatus): 'denied' | 'unavailable' | 'browser_unsupported' | null {
  if (status === 'denied') return 'denied';
  if (status === 'unavailable') return 'unavailable';
  return null;
}

export function WebcamCheckPanel({
  storageKey,
  onPassed,
  onFailed,
  required = true,
  className,
}: WebcamCheckPanelProps) {
  const { status, errorMessage, stream, requestPermission } = useWebcamPreflight({ storageKey });
  const videoRef = useRef<HTMLVideoElement | null>(null);

  const passedRef = useRef(false);
  const lastFailReasonRef = useRef<string | null>(null);
  const [hasInteracted, setHasInteracted] = useState(false);

  // Pipe the stream into the <video> preview whenever it changes.
  useEffect(() => {
    const el = videoRef.current;
    if (!el) return;
    if (stream) {
      try {
        el.srcObject = stream;
        void el.play().catch(() => undefined);
      } catch {
        // ignored — preview is informational
      }
    } else {
      try {
        el.srcObject = null;
      } catch {
        // ignored
      }
    }
  }, [stream]);

  // Notify the parent on status transitions.
  useEffect(() => {
    if (status === 'granted' && !passedRef.current) {
      passedRef.current = true;
      try {
        onPassed();
      } catch {
        // never let consumer errors break the panel
      }
      return;
    }

    if (status === 'denied' || status === 'unavailable') {
      const reason = statusReason(status);
      if (reason && lastFailReasonRef.current !== reason) {
        lastFailReasonRef.current = reason;
        try {
          onFailed(reason);
        } catch {
          // ignored
        }
      }
    }
  }, [status, onPassed, onFailed]);

  const handleTest = () => {
    setHasInteracted(true);
    void requestPermission();
  };

  const isBusy = status === 'requesting';
  const isPassed = status === 'granted';
  const isBlocked = status === 'denied' || status === 'unavailable';

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className={cn('flex flex-col gap-4', className)}
    >
      <div className="bg-primary/5 border border-primary/20 rounded-2xl p-4 text-sm text-navy shadow-sm">
        <p className="flex items-start gap-2">
          <Video className="w-5 h-5 text-primary shrink-0" aria-hidden />
          <span>
            We need to verify your webcam before this Speaking section. The camera
            stays on for the duration of the exam-mode mock.{' '}
            {required ? (
              <span className="font-semibold">This check is mandatory.</span>
            ) : null}
          </span>
        </p>
      </div>

      <div
        className={cn(
          'flex flex-col gap-3 p-4 rounded-xl border transition-colors',
          isPassed && 'border-emerald-200 bg-emerald-50/50',
          isBlocked && 'border-red-200 bg-red-50/50',
          !isPassed && !isBlocked && 'border-border bg-background-light',
        )}
      >
        <div className="flex items-center gap-3">
          <div
            className={cn(
              'w-10 h-10 rounded-full flex items-center justify-center shrink-0 shadow-sm transition-transform duration-300',
              isPassed && 'bg-emerald-100 text-emerald-600',
              isBlocked && 'bg-red-100 text-red-600',
              isBusy && 'bg-primary text-white dark:bg-violet-700 scale-110',
              !isPassed && !isBlocked && !isBusy && 'bg-primary/10 text-primary',
            )}
          >
            {isPassed ? (
              <CheckCircle2 className="w-5 h-5" aria-hidden />
            ) : isBlocked ? (
              <VideoOff className="w-5 h-5" aria-hidden />
            ) : isBusy ? (
              <Loader2 className="w-5 h-5 animate-spin" aria-hidden />
            ) : (
              <Video className="w-5 h-5" aria-hidden />
            )}
          </div>

          <div className="flex-1">
            <p className="text-sm font-semibold text-navy">Camera check</p>
            {isPassed ? (
              <p className="text-xs text-emerald-600 font-medium tracking-wide">
                Passed. Webcam is streaming.
              </p>
            ) : isBusy ? (
              <p className="text-xs text-primary font-medium">
                Waiting for browser permission…
              </p>
            ) : isBlocked ? (
              <p className="text-xs text-red-600 font-medium">
                {status === 'unavailable'
                  ? 'No camera detected on this device.'
                  : 'Camera blocked. Enable it to continue.'}
              </p>
            ) : (
              <p className="text-xs text-muted">
                Press <span className="font-semibold">Test camera</span> to grant
                permission.
              </p>
            )}
          </div>

          {!isPassed ? (
            <Button
              size="sm"
              variant={isBlocked ? 'outline' : 'primary'}
              onClick={handleTest}
              loading={isBusy}
              disabled={isBusy}
              className="shadow-sm"
            >
              {isBlocked ? 'Retry' : isBusy ? 'Requesting…' : 'Test camera'}
            </Button>
          ) : null}
        </div>

        <div
          className={cn(
            'w-full overflow-hidden rounded-lg border border-border bg-background-dark/90 aspect-video flex items-center justify-center',
            isPassed ? 'opacity-100' : 'opacity-60',
          )}
        >
          {/* The video element is always mounted so the ref is stable; the
              stream gets attached imperatively inside the useEffect above. */}
          <video
            ref={videoRef}
            playsInline
            muted
            autoPlay
            className={cn('w-full h-full object-cover', !stream && 'hidden')}
            aria-label="Webcam preview"
          />
          {!stream ? (
            <div className="flex flex-col items-center gap-1 text-white/70 text-xs">
              <VideoOff className="w-6 h-6" aria-hidden />
              <span>Preview appears here after you grant access</span>
            </div>
          ) : null}
        </div>
      </div>

      {isBlocked && hasInteracted ? (
        <InlineAlert variant="error" className="shadow-sm rounded-xl border-red-200">
          {errorMessage ??
            (status === 'unavailable'
              ? 'No camera was detected. Connect a webcam and retry.'
              : 'Camera blocked. Enable it to continue. Check your browser site permissions, then press Retry.')}
        </InlineAlert>
      ) : null}
    </motion.div>
  );
}
