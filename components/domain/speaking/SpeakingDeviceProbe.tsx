'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, Loader2, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';

/**
 * 2026-05-27 audit fix — RULE_60 (CBT environment requirements) was previously
 * a checkbox-only self-attestation. This component actively enumerates the
 * candidate's audio + video devices and flags anything whose label matches the
 * forbidden patterns (Bluetooth, AirPods, Beats, Sony WF/WH, Jabra, Bose QC).
 *
 * It is a soft gate (advisory) by design — Bluetooth labels are not reliable
 * across all browsers and operating systems, and the ProProctor application
 * is the authoritative enforcer at exam-day. But the audit explicitly asked
 * for at least an opt-in device probe in the pre-exam screen, and this is it.
 */

const FORBIDDEN_AUDIO = /\b(bluetooth|airpods|beats|wireless|sony wf|sony wh|jabra|bose qc)\b/i;
const FORBIDDEN_VIDEO = /\b(bluetooth|wireless)\b/i;

export interface SpeakingDeviceProbeResult {
  ran: boolean;
  audioInputs: Array<{ label: string; flagged: boolean }>;
  audioOutputs: Array<{ label: string; flagged: boolean }>;
  videoInputs: Array<{ label: string; flagged: boolean }>;
  multipleMonitors: boolean | null;
  ok: boolean;
}

export interface SpeakingDeviceProbeProps {
  onResult?: (result: SpeakingDeviceProbeResult) => void;
}

export function SpeakingDeviceProbe({ onResult }: SpeakingDeviceProbeProps) {
  const [status, setStatus] = useState<'idle' | 'running' | 'done' | 'error'>('idle');
  const [result, setResult] = useState<SpeakingDeviceProbeResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.enumerateDevices) {
      setError('This browser does not support device enumeration. Try Chrome or Edge.');
      setStatus('error');
      return;
    }
    setStatus('running');
    setError(null);
    try {
      // Request permissions first so the browser exposes device labels.
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
        stream.getTracks().forEach((t) => t.stop());
      } catch {
        // If the user denies, we can still enumerate but most labels will be empty.
      }
      const devices = await navigator.mediaDevices.enumerateDevices();
      const audioInputs = devices
        .filter((d) => d.kind === 'audioinput' && d.label)
        .map((d) => ({ label: d.label, flagged: FORBIDDEN_AUDIO.test(d.label) }));
      const audioOutputs = devices
        .filter((d) => d.kind === 'audiooutput' && d.label)
        .map((d) => ({ label: d.label, flagged: FORBIDDEN_AUDIO.test(d.label) }));
      const videoInputs = devices
        .filter((d) => d.kind === 'videoinput' && d.label)
        .map((d) => ({ label: d.label, flagged: FORBIDDEN_VIDEO.test(d.label) }));

      // RULE_60 — single monitor only. window.screen.isExtended is the modern
      // signal; not all browsers expose it. Returns null when unknown.
      let multipleMonitors: boolean | null = null;
      const ws = typeof window !== 'undefined' ? (window.screen as unknown as { isExtended?: boolean }) : null;
      if (ws && typeof ws.isExtended === 'boolean') multipleMonitors = ws.isExtended;

      const flaggedCount = audioInputs.filter((d) => d.flagged).length
        + audioOutputs.filter((d) => d.flagged).length
        + videoInputs.filter((d) => d.flagged).length;
      const ok = flaggedCount === 0 && multipleMonitors !== true;
      const next: SpeakingDeviceProbeResult = {
        ran: true,
        audioInputs,
        audioOutputs,
        videoInputs,
        multipleMonitors,
        ok,
      };
      setResult(next);
      onResult?.(next);
      setStatus('done');
    } catch (e) {
      setError((e as Error).message ?? 'Device probe failed.');
      setStatus('error');
    }
  }, [onResult]);

  // Run once on mount.
  useEffect(() => {
    void run();
  }, [run]);

  return (
    <div className="rounded-2xl border border-border bg-surface p-4 space-y-3" data-testid="speaking-device-probe">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          {status === 'running' ? (
            <Loader2 className="h-4 w-4 animate-spin text-muted" />
          ) : status === 'done' && result?.ok ? (
            <CheckCircle2 className="h-4 w-4 text-success" />
          ) : (
            <AlertTriangle className="h-4 w-4 text-warning" />
          )}
          <p className="text-sm font-bold text-navy">Live device probe (RULE_60)</p>
        </div>
        <Button variant="ghost" size="sm" onClick={() => void run()} aria-label="Re-run device probe">
          <RefreshCw className="h-3 w-3 mr-1" /> Re-run
        </Button>
      </div>

      {status === 'running' ? (
        <p className="text-xs text-muted">Probing audio + video devices… you may see a permission prompt.</p>
      ) : null}

      {error ? (
        <p className="text-xs text-error" role="alert">{error}</p>
      ) : null}

      {result ? (
        <div className="space-y-2 text-xs">
          <DeviceList title="Microphones" entries={result.audioInputs} />
          <DeviceList title="Speakers / output" entries={result.audioOutputs} />
          <DeviceList title="Webcams" entries={result.videoInputs} />
          {result.multipleMonitors === true ? (
            <p className="text-error" role="alert">
              Multiple monitors detected. RULE_60 requires a single monitor only. Disconnect or disable the second display before exam start.
            </p>
          ) : null}
          {result.ok ? (
            <p className="text-success">All detected devices satisfy RULE_60. (Browser-level probe is advisory; ProProctor remains the authoritative gate.)</p>
          ) : (
            <p className="text-warning">
              Devices flagged for review. Bluetooth / wireless audio is forbidden during Speaking (RULE_60). Switch to a wired or built-in device.
            </p>
          )}
        </div>
      ) : null}
    </div>
  );
}

function DeviceList({ title, entries }: { title: string; entries: Array<{ label: string; flagged: boolean }> }) {
  if (entries.length === 0) {
    return (
      <p>
        <span className="font-semibold">{title}:</span> none detected.
      </p>
    );
  }
  return (
    <div>
      <p className="font-semibold">{title}:</p>
      <ul className="ml-4 space-y-0.5">
        {entries.map((d, i) => (
          <li key={`${title}-${i}`} className={d.flagged ? 'text-error' : 'text-navy'}>
            {d.flagged ? '🚫' : '✓'} {d.label}
          </li>
        ))}
      </ul>
    </div>
  );
}
