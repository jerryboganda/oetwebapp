'use client';

import { useTranslations } from 'next-intl';
import { AlertTriangle, CheckCircle2, CloudOff, HardDrive, Loader2 } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { DraftSyncState } from '@/hooks/use-writing-draft-sync';

/**
 * Another tab or device saved a different letter: the learner picks a version
 * (nothing is saved until then); after "Use the other version" their own text
 * stays one click away.
 */
export function DraftConflictNotice({
  conflict,
  previousText,
  onKeepThis,
  onUseOther,
  onRestorePrevious,
  onDismissPrevious,
}: {
  conflict: boolean;
  previousText: string | null;
  onKeepThis: () => void;
  onUseOther: () => void;
  onRestorePrevious: () => void;
  onDismissPrevious: () => void;
}) {
  const t = useTranslations();
  return (
    <>
      {conflict ? (
        <InlineAlert
          variant="warning"
          title={t('writing.practice.session.draft.conflictTitle')}
          data-testid="writing-draft-conflict"
          action={
            <div className="flex flex-wrap gap-2">
              <Button size="sm" onClick={() => onKeepThis()}>
                {t('writing.practice.session.draft.keepThis')}
              </Button>
              <Button size="sm" variant="outline" onClick={() => onUseOther()}>
                {t('writing.practice.session.draft.useOther')}
              </Button>
            </div>
          }
        >
          {t('writing.practice.session.draft.conflictBody')}
        </InlineAlert>
      ) : null}
      {previousText !== null ? (
        <InlineAlert
          variant="info"
          live="polite"
          dismissible
          onDismiss={onDismissPrevious}
          action={
            <Button size="sm" variant="outline" onClick={() => onRestorePrevious()}>
              {t('writing.practice.session.draft.restorePrevious')}
            </Button>
          }
        >
          {t('writing.practice.session.draft.switched')}
        </InlineAlert>
      ) : null}
    </>
  );
}

/** Where the learner's letter is right now: on the server, on its way, or only on this device. */
export function DraftSaveStatus({ state, className }: { state: DraftSyncState; className?: string }) {
  const t = useTranslations();
  let label: string;
  let Icon = CheckCircle2;
  let tone = 'text-success-strong';
  switch (state) {
    case 'saving':
      label = t('writing.practice.session.draft.saving');
      Icon = Loader2;
      tone = 'text-muted';
      break;
    case 'pending-local':
      label = t('writing.practice.session.draft.pendingLocal');
      Icon = HardDrive;
      tone = 'text-warning-strong';
      break;
    case 'offline':
      label = t('writing.practice.session.draft.offline');
      Icon = CloudOff;
      tone = 'text-warning-strong';
      break;
    case 'error':
      label = t('writing.practice.session.draft.error');
      Icon = AlertTriangle;
      tone = 'text-danger-strong';
      break;
    default:
      label = t('writing.practice.session.draft.saved');
  }
  return (
    <span
      data-testid="writing-draft-status"
      data-state={state}
      role="status"
      aria-live="polite"
      className={cn('inline-flex items-center gap-1.5 text-xs font-semibold', tone, className)}
    >
      <Icon className={cn('h-3.5 w-3.5 shrink-0', state === 'saving' && 'animate-spin')} aria-hidden="true" />
      {label}
    </span>
  );
}
