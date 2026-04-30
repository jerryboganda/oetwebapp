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

  return (
    <div
      role="note"
      aria-label="Speaking score disclaimer"
      className={
        'flex items-start gap-3 rounded-lg border border-warning/30 bg-warning/10 p-3 text-sm text-navy ' +
        (className ?? '')
      }
    >
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
      <p className="leading-relaxed">{text}</p>
    </div>
  );
}

export default SpeakingScoreDisclaimer;
