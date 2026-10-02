'use client';

// Wave 7 of docs/SPEAKING-MODULE-PLAN.md - standardised
// "Estimated score, not official OET" banner shown on every speaking
// results / readiness surface. The exact wording is operator-tunable
// via the `Speaking:Compliance` config section and exposed at
// `/v1/speaking/compliance`. We try to load that text once per mount
// and fall back to a sensible static copy if the call fails (so the
// banner is *always* present — that's a hard requirement).
import { useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';

import { apiClient } from '@/lib/api';
import { cn } from '@/lib/utils';

const FALLBACK_TEXT =
  'Estimated score, not an official OET result. Use this as a practice indicator only.';

interface ComplianceCopy {
  scoreDisclaimer?: unknown;
}

export interface SpeakingScoreDisclaimerProps {
  className?: string;
}

export function SpeakingScoreDisclaimer({ className }: SpeakingScoreDisclaimerProps) {
  const [text, setText] = useState<string>(FALLBACK_TEXT);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .request<ComplianceCopy>('/v1/speaking/compliance')
      .then((data) => {
        if (cancelled) return;
        if (typeof data.scoreDisclaimer === 'string' && data.scoreDisclaimer.trim().length > 0) {
          setText(data.scoreDisclaimer);
        }
      })
      .catch(() => {
        // Silent: we keep the static fallback so the banner is always shown.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Same shape as InlineAlert, so it sits flush with the method note above it.
  return (
    <div
      role="note"
      aria-label="Speaking score disclaimer"
      className={cn('flex items-start gap-3 rounded-2xl border border-warning/30 bg-warning/10 px-4 py-4 text-sm text-navy shadow-sm', className)}
    >
      <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden />
      <p className="leading-relaxed">{text}</p>
    </div>
  );
}

export default SpeakingScoreDisclaimer;
