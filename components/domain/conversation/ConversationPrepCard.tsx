'use client';

import { Mic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type { ConversationScenario } from '@/lib/types/conversation';

interface Props {
  scenario: ConversationScenario;
  prepCountdown: number;
  recordingConsentAccepted?: boolean;
  vendorConsentAccepted?: boolean;
  consentVersion?: string;
  audioRetentionDays?: number;
  startDisabled?: boolean;
  onConsentChange?: (key: 'recording' | 'vendor', accepted: boolean) => void;
  onStart: () => void;
}

function formatTime(s: number) {
  return `${Math.floor(s / 60).toString().padStart(2, '0')}:${(s % 60).toString().padStart(2, '0')}`;
}

export function ConversationPrepCard({
  scenario,
  prepCountdown,
  recordingConsentAccepted = false,
  vendorConsentAccepted = false,
  consentVersion = 'realtime-stt-v1-2026-05-14',
  audioRetentionDays = 30,
  startDisabled = false,
  onConsentChange,
  onStart,
}: Props) {
  const objectives = scenario.objectives ?? [];
  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <div className="mb-2 text-xs font-bold uppercase tracking-[0.2em] text-primary">
        Preparation phase
      </div>
      <h1 className="mb-2 text-2xl font-bold text-navy">{scenario.title}</h1>
      {scenario.context && <p className="mb-4 text-muted">{scenario.context}</p>}

      <div className="mb-4 grid grid-cols-1 gap-3 md:grid-cols-2">
        <div className="rounded-2xl border border-border bg-background-light p-3">
          <div className="mb-1 text-xs font-semibold uppercase text-muted">Your role</div>
          <div className="text-sm font-bold text-navy">
            {scenario.clinicianRole ?? 'Clinician'}
          </div>
        </div>
        <div className="rounded-2xl border border-border bg-background-light p-3">
          <div className="mb-1 text-xs font-semibold uppercase text-muted">
            {scenario.taskTypeCode === 'oet-handover' ? 'Colleague' : 'Patient'}
          </div>
          <div className="text-sm font-bold text-navy">
            {scenario.patientRole ?? 'Patient'}
          </div>
        </div>
      </div>

      {objectives.length > 0 && (
        <div className="mb-4 rounded-2xl border border-border bg-background-light p-3">
          <div className="mb-2 text-xs font-semibold uppercase text-muted">Objectives</div>
          <ul className="space-y-1">
            {objectives.map((objective, i) => (
              <li key={i} className="flex items-start gap-2 text-sm text-navy/80">
                <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  {i + 1}
                </span>
                {objective}
              </li>
            ))}
          </ul>
        </div>
      )}

      {scenario.expectedRedFlags && scenario.expectedRedFlags.length > 0 && (
        <div className="mb-4 rounded-2xl border border-danger/20 bg-danger/5 p-3 text-danger">
          <div className="mb-1 text-xs font-semibold uppercase">
            Watch for red flags
          </div>
          <ul className="list-disc space-y-0.5 pl-5 text-xs">
            {scenario.expectedRedFlags.map((flag, i) => (<li key={i}>{flag}</li>))}
          </ul>
        </div>
      )}

      <div className="mb-4 rounded-2xl border border-border bg-background-light p-3">
        <div className="mb-2 text-xs font-semibold uppercase text-muted">Recording and transcription consent</div>
        <label className="flex items-start gap-2 text-sm text-navy/80">
          <input
            type="checkbox"
            checked={recordingConsentAccepted}
            onChange={(event) => onConsentChange?.('recording', event.target.checked)}
            className="mt-1 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>I consent to microphone capture and {audioRetentionDays}-day retention of final conversation audio for this practice feature.</span>
        </label>
        <label className="mt-2 flex items-start gap-2 text-sm text-navy/80">
          <input
            type="checkbox"
            checked={vendorConsentAccepted}
            onChange={(event) => onConsentChange?.('vendor', event.target.checked)}
            className="mt-1 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <span>I consent to speech processing by the configured transcription provider under policy {consentVersion}.</span>
        </label>
      </div>

      <div className="flex items-center justify-between">
        <div className="tabular-nums text-3xl font-bold text-primary">
          {formatTime(prepCountdown)}
        </div>
        <Button onClick={onStart} type="button" disabled={startDisabled} variant="primary">
          <Mic className="h-4 w-4" /> Start now
        </Button>
      </div>
    </section>
  );
}
