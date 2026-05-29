'use client';

import { AlertTriangle } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';

interface ReadingPartAWarningProps {
  isOpen: boolean;
  remainingSeconds: number;
  onDismiss: () => void;
  onNavigateToPartB: () => void;
}

export function ReadingPartAWarning({
  isOpen,
  remainingSeconds,
  onDismiss,
  onNavigateToPartB,
}: ReadingPartAWarningProps) {
  return (
    <Modal open={isOpen} onClose={onDismiss} title="Part A closing soon" size="sm">
      <div className="space-y-5">
        <div className="flex gap-3 rounded-xl border border-amber-200 bg-amber-50 p-4 dark:border-amber-800 dark:bg-amber-950/30">
          <span className="mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-amber-100 text-amber-600 dark:bg-amber-900/50 dark:text-amber-400">
            <AlertTriangle className="h-4 w-4" aria-hidden="true" />
          </span>
          <div className="text-sm leading-6 text-slate-700 dark:text-slate-300">
            <p className="font-medium text-amber-800 dark:text-amber-300">
              Part A closes in {remainingSeconds} second{remainingSeconds !== 1 ? 's' : ''}.
            </p>
            <p className="mt-1 text-muted">
              Any unanswered questions will be saved as blank. You will be moved to Part B automatically.
            </p>
          </div>
        </div>
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="outline" onClick={onDismiss}>
            Continue Part A
          </Button>
          <Button type="button" variant="primary" onClick={onNavigateToPartB}>
            Go to Part B now
          </Button>
        </div>
      </div>
    </Modal>
  );
}
