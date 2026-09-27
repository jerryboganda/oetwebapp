'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { describeOwnerAgentError } from '@/lib/owner-agent/api';
import type { CreateSession, Mode, SessionDetail } from '@/lib/owner-agent/types';
import {
  EngineModelEffortPicker,
  normalizePickerValue,
  type EngineCapabilities,
  type EngineModelEffortValue,
} from './EngineModelEffortPicker';
import { ModeToggle } from './ModeToggle';
import { isStepUpCancelled, useStepUp } from './StepUpDialog';

export interface NewSessionDialogProps {
  open: boolean;
  engines: EngineCapabilities;
  onClose: () => void;
  /** `stepUpToken` is supplied (and required) when starting in Autopilot. */
  onCreate: (body: CreateSession, stepUpToken?: string) => Promise<SessionDetail>;
  onCreated?: (session: SessionDetail) => void;
}

/**
 * Engine/model/effort (from live capabilities), mode, title and an optional
 * first message. Starting in Autopilot asks for a fresh authenticator code
 * first (the API consumes a step-up for it, like switching to Autopilot).
 */
export function NewSessionDialog({ open, engines, onClose, onCreate, onCreated }: NewSessionDialogProps) {
  const requestStepUp = useStepUp();
  const [picker, setPicker] = useState<EngineModelEffortValue | null>(null);
  const [mode, setMode] = useState<Mode>('guarded');
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setPicker((current) => normalizePickerValue(engines, current));
    setError(null);
  }, [open, engines]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!picker || busy) return;
    setBusy(true);
    setError(null);
    try {
      const body: CreateSession = { engine: picker.engine, model: picker.model, mode };
      if (picker.effort) body.effort = picker.effort;
      if (title.trim()) body.title = title.trim().slice(0, 200);
      if (message.trim()) body.initialMessage = message;
      const stepUpToken = mode === 'autopilot' ? await requestStepUp('Start in Autopilot') : undefined;
      const session = await onCreate(body, stepUpToken);
      setTitle('');
      setMessage('');
      onCreated?.(session);
    } catch (err) {
      if (!isStepUpCancelled(err)) setError(describeOwnerAgentError(err, 'Could not start the session.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal open={open} onClose={() => (busy ? undefined : onClose())} title="New agent session" size="lg">
      <form onSubmit={(event) => void submit(event)} className="space-y-4 text-sm" data-testid="new-session-form">
        <EngineModelEffortPicker engines={engines} value={picker} onChange={setPicker} disabled={busy} />
        <div>
          <p className="mb-1 text-xs font-medium text-admin-fg-muted">Mode</p>
          <ModeToggle value={mode} onChange={setMode} disabled={busy} />
        </div>
        <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-new-title">
          Title (optional)
        </label>
        <input
          id="owner-agent-new-title"
          value={title}
          maxLength={200}
          onChange={(event) => setTitle(event.target.value)}
          className="h-9 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 text-sm"
          disabled={busy}
        />
        <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-new-message">
          First message (optional)
        </label>
        <textarea
          id="owner-agent-new-message"
          value={message}
          rows={5}
          onChange={(event) => setMessage(event.target.value)}
          className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
          placeholder="What should the agent do? It works on its own agent/* branch."
          disabled={busy}
        />
        {error ? (
          <p className="text-xs text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" size="sm" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" size="sm" loading={busy} disabled={!picker}>
            Start session
          </Button>
        </div>
      </form>
    </Modal>
  );
}
