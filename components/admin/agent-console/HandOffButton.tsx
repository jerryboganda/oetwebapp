'use client';

import { useState } from 'react';
import { ArrowLeftRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { OWNER_AGENT_ENGINES, type Engine } from '@/lib/owner-agent/types';
import {
  ENGINE_LABEL,
  EngineModelEffortPicker,
  isEngineAvailable,
  normalizePickerValue,
  type EngineCapabilities,
  type EngineModelEffortValue,
} from './EngineModelEffortPicker';

export interface HandOffButtonProps {
  engines: EngineCapabilities;
  currentEngine: Engine;
  disabled?: boolean;
  /** Creates the new session (same worktree/branch, seeded with a summary). */
  onHandoff: (value: EngineModelEffortValue) => Promise<void>;
}

/** Continue this session's work on the other engine (or another model). */
export function HandOffButton({ engines, currentEngine, disabled = false, onHandoff }: HandOffButtonProps) {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState<EngineModelEffortValue | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const openDialog = () => {
    const other = OWNER_AGENT_ENGINES.find((engine) => engine !== currentEngine && isEngineAvailable(engines?.[engine]));
    setValue(normalizePickerValue(engines, { engine: other ?? currentEngine }));
    setError(null);
    setOpen(true);
  };

  const confirm = async () => {
    if (!value || busy) return;
    setBusy(true);
    setError(null);
    try {
      await onHandoff(value);
      setOpen(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Hand-off failed.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Button variant="outline" size="sm" onClick={openDialog} disabled={disabled}>
        <ArrowLeftRight className="h-4 w-4" aria-hidden="true" /> Hand off
      </Button>
      <Modal open={open} onClose={() => (busy ? undefined : setOpen(false))} title="Hand off to another engine" size="md">
        <div className="space-y-4 text-sm">
          <p className="text-admin-fg-muted">
            Starts a new {value ? ENGINE_LABEL[value.engine] : ''} session on the same worktree and branch, seeded with a summary of this
            one. Switching engines drops the prompt cache, so the first turn there costs more quota.
          </p>
          <EngineModelEffortPicker engines={engines} value={value} onChange={setValue} />
          {error ? (
            <p className="text-xs text-red-700 dark:text-red-300" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={() => setOpen(false)} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" size="sm" onClick={() => void confirm()} loading={busy} disabled={!value}>
              Hand off
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}
