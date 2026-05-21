'use client';

import { Volume2 } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';

interface ListeningStrictLayerProps {
  onePlay?: boolean;
  transcriptHidden?: boolean;
  pauseAllowed?: boolean;
  onReportIssue?: () => void;
}

export function ListeningStrictLayer({
  onePlay = true,
  transcriptHidden = true,
  pauseAllowed = false,
  onReportIssue,
}: ListeningStrictLayerProps) {
  return (
    <div className="rounded-2xl border border-primary/20 bg-primary/5 p-4">
      <div className="flex items-start gap-3">
        <Volume2 className="mt-0.5 h-5 w-5 text-primary" aria-hidden />
        <div>
          <p className="text-sm font-black text-navy">Listening exam-mode locks</p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm leading-6 text-muted">
            {onePlay ? <li>Audio is one-play only; replay controls stay disabled.</li> : null}
            {transcriptHidden ? <li>Transcript remains hidden until submission/review release.</li> : null}
            {!pauseAllowed ? <li>Pause is disabled except for admin-approved interruption handling.</li> : null}
          </ul>
        </div>
      </div>
      <InlineAlert className="mt-3" variant="info">
        If audio is genuinely inaudible, report it immediately. Reports are audited but do not auto-block submission.
      </InlineAlert>
      {onReportIssue ? (
        <Button className="mt-3" variant="secondary" size="sm" onClick={onReportIssue}>
          Report audio issue
        </Button>
      ) : null}
    </div>
  );
}
