'use client';

import { Eye, ShieldCheck, Zap } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { Mode } from '@/lib/owner-agent/types';

export const MODE_LABEL: Record<Mode, string> = {
  read_only: 'Read-only',
  guarded: 'Guarded',
  autopilot: 'Autopilot',
};

const MODE_HINT: Record<Mode, string> = {
  read_only: 'Reads only; every write is denied.',
  guarded: 'Destructive or unparseable actions wait for your approval.',
  autopilot: 'Destructive actions run after a snapshot. Tainted turns still ask. Needs a fresh authenticator code.',
};

const MODE_ICON: Record<Mode, typeof Eye> = {
  read_only: Eye,
  guarded: ShieldCheck,
  autopilot: Zap,
};

export interface ModeToggleProps {
  value: Mode;
  onChange: (mode: Mode) => void;
  disabled?: boolean;
  /** Hide Autopilot (e.g. when creating a session — it is enabled later with a step-up). */
  allowAutopilot?: boolean;
  tainted?: boolean;
  taintReasons?: string[];
  pendingMode?: Mode | null;
  className?: string;
}

/**
 * Read-only / Guarded / Autopilot segmented control with the taint badge. The
 * parent performs the step-up before switching to Autopilot.
 */
export function ModeToggle({
  value,
  onChange,
  disabled = false,
  allowAutopilot = true,
  tainted = false,
  taintReasons = [],
  pendingMode = null,
  className,
}: ModeToggleProps) {
  const modes: Mode[] = allowAutopilot ? ['read_only', 'guarded', 'autopilot'] : ['read_only', 'guarded'];
  return (
    <div className={cn('flex flex-wrap items-center gap-2', className)}>
      <div role="radiogroup" aria-label="Session mode" className="inline-flex rounded-lg border border-admin-border bg-admin-bg-subtle p-0.5">
        {modes.map((mode) => {
          const Icon = MODE_ICON[mode];
          const selected = value === mode;
          return (
            <button
              key={mode}
              type="button"
              role="radio"
              aria-checked={selected}
              title={MODE_HINT[mode]}
              disabled={disabled || pendingMode !== null}
              onClick={() => {
                if (!selected) onChange(mode);
              }}
              className={cn(
                'inline-flex items-center gap-1 rounded-md px-2.5 py-1 text-xs font-medium transition-colors disabled:opacity-50',
                selected
                  ? mode === 'autopilot'
                    ? 'bg-amber-500 text-slate-950'
                    : 'bg-admin-bg-surface text-admin-fg-strong shadow-sm'
                  : 'text-admin-fg-muted hover:text-admin-fg-default',
              )}
            >
              <Icon className="h-3.5 w-3.5" aria-hidden="true" />
              {pendingMode === mode ? `${MODE_LABEL[mode]}…` : MODE_LABEL[mode]}
            </button>
          );
        })}
      </div>
      {tainted ? (
        <Badge variant="danger" title={taintReasons.length > 0 ? taintReasons.join('\n') : 'This turn read untrusted content.'} data-testid="taint-badge">
          Tainted
        </Badge>
      ) : null}
    </div>
  );
}
