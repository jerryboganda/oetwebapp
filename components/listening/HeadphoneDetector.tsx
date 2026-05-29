'use client';

import { useEffect, useState } from 'react';
import { Headphones } from 'lucide-react';

export type HeadphoneDetectionStatus = 'unknown' | 'detected' | 'not_detected';

export interface HeadphoneDetectionResult {
  status: HeadphoneDetectionStatus;
  deviceName: string | null;
}

export function useHeadphoneDetect(): HeadphoneDetectionResult {
  const [result, setResult] = useState<HeadphoneDetectionResult>({ status: 'unknown', deviceName: null });

  useEffect(() => {
    let cancelled = false;

    async function detect() {
      if (typeof navigator === 'undefined' || !navigator.mediaDevices?.enumerateDevices) return;

      try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        if (cancelled) return;
        const labelledOutputs = devices
          .filter((device) => device.kind === 'audiooutput')
          .filter((device) => device.label.trim().length > 0);
        const detected = labelledOutputs.find((device) => /head(phone|set)|ear(bud|phone)|wired/i.test(device.label));
        if (detected) {
          setResult({ status: 'detected', deviceName: detected.label });
        } else if (labelledOutputs.length > 0) {
          setResult({ status: 'not_detected', deviceName: null });
        } else {
          setResult({ status: 'unknown', deviceName: null });
        }
      } catch {
        if (!cancelled) setResult({ status: 'unknown', deviceName: null });
      }
    }

    void detect();
    navigator.mediaDevices?.addEventListener?.('devicechange', detect);
    return () => {
      cancelled = true;
      navigator.mediaDevices?.removeEventListener?.('devicechange', detect);
    };
  }, []);

  return result;
}

export interface HeadphoneDetectorProps {
  className?: string;
}

export function HeadphoneDetector({ className }: HeadphoneDetectorProps) {
  const detection = useHeadphoneDetect();
  const label = detection.status === 'detected'
    ? 'Headphones detected'
    : detection.status === 'not_detected'
      ? 'Headphones not detected'
      : 'Headphones recommended';

  return (
    <div
      role="status"
      aria-live="polite"
      className={[
        'inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium',
        'bg-background-light text-muted',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <Headphones aria-hidden="true" className="h-3.5 w-3.5" />
      <span>{label}</span>
      {detection.deviceName ? <span className="sr-only">: {detection.deviceName}</span> : null}
    </div>
  );
}

export default HeadphoneDetector;
