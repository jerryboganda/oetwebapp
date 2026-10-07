'use client';

import { useId } from 'react';
import { AlertTriangle } from 'lucide-react';
import { cn } from '@/lib/utils';
import {
  OWNER_AGENT_ENGINES,
  type ConsoleStatus,
  type Engine,
  type EngineStatus,
  type ModelInfo,
} from '@/lib/owner-agent/types';

export interface EngineModelEffortValue {
  engine: Engine;
  model: string;
  effort?: string;
}

export type EngineCapabilities = ConsoleStatus['engines'] | null | undefined;

export const ENGINE_LABEL: Record<Engine, string> = {
  claude: 'Claude Code',
  codex: 'Codex',
  opencode: 'Direct OpenCode gateway',
};

/** An engine is selectable only when signed in and reporting at least one model. */
export function isEngineAvailable(status: EngineStatus | null | undefined): boolean {
  return Boolean(status && status.auth?.state === 'signed_in' && Array.isArray(status.models) && status.models.length > 0);
}

export function findModel(engines: EngineCapabilities, engine: Engine, model: string): ModelInfo | undefined {
  return engines?.[engine]?.models?.find((candidate) => candidate.value === model);
}

/** Effort options for a model — empty when the model does not support effort. */
export function effortOptionsFor(model: ModelInfo | undefined): string[] {
  if (!model || !model.supportsEffort || !Array.isArray(model.efforts)) return [];
  return model.efforts.filter((effort) => typeof effort === 'string' && effort.length > 0);
}

function pickEffort(model: ModelInfo | undefined, preferred: string | undefined): string | undefined {
  const efforts = effortOptionsFor(model);
  if (efforts.length === 0) return undefined;
  if (preferred && efforts.includes(preferred)) return preferred;
  if (model?.defaultEffort && efforts.includes(model.defaultEffort)) return model.defaultEffort;
  return efforts[0];
}

/**
 * Coerce a (possibly stale) selection onto what the engines currently report:
 * unknown engine → first available; unknown model → engine's first model;
 * effort dropped when unsupported, defaulted when missing or no longer offered.
 * Returns null when no engine is usable.
 */
export function normalizePickerValue(
  engines: EngineCapabilities,
  value: Partial<EngineModelEffortValue> | null | undefined,
  options: { lockEngine?: boolean } = {},
): EngineModelEffortValue | null {
  if (!engines) return null;
  let engine: Engine | undefined = value?.engine;
  if (!engine || (!options.lockEngine && !isEngineAvailable(engines[engine]))) {
    engine = OWNER_AGENT_ENGINES.find((candidate) => isEngineAvailable(engines[candidate]));
  }
  if (!engine) return null;
  const models = engines[engine]?.models ?? [];
  if (models.length === 0) return null;
  const model = models.find((candidate) => candidate.value === value?.model) ?? models[0];
  const effort = pickEffort(model, value?.effort);
  return effort ? { engine, model: model.value, effort } : { engine, model: model.value };
}

export interface EngineModelEffortPickerProps {
  engines: EngineCapabilities;
  value: EngineModelEffortValue | null;
  onChange: (value: EngineModelEffortValue) => void;
  /** Composer mode: the session's engine is fixed (switching engines = hand-off). */
  lockEngine?: boolean;
  /** The session's current model/effort — shows a cache-loss warning when changed. */
  sessionModel?: { model: string; effort?: string } | null;
  disabled?: boolean;
  compact?: boolean;
  className?: string;
}

const selectClass =
  'h-9 w-full min-w-0 rounded-lg border border-admin-border bg-admin-bg-surface px-2 text-xs text-admin-fg-default disabled:opacity-50';

/**
 * Engine → model → effort selects populated ONLY from the live `/status`
 * capabilities (opaque ids; nothing hard-coded). Effort is hidden for models
 * that do not support it.
 */
export function EngineModelEffortPicker({
  engines,
  value,
  onChange,
  lockEngine = false,
  sessionModel,
  disabled = false,
  compact = false,
  className,
}: EngineModelEffortPickerProps) {
  const baseId = useId();
  const current = value ?? normalizePickerValue(engines, null);

  if (!engines || !current) {
    return (
      <p className={cn('text-xs text-admin-fg-muted', className)} data-testid="picker-unavailable">
        No engine is ready yet. Connect Claude Code or Codex, or configure Direct OpenCode gateway in Settings.
      </p>
    );
  }

  const models = engines[current.engine]?.models ?? [];
  const selectedModel = models.find((m) => m.value === current.model);
  const efforts = effortOptionsFor(selectedModel);
  const switching = Boolean(
    sessionModel && (sessionModel.model !== current.model || (efforts.length > 0 && (sessionModel.effort ?? '') !== (current.effort ?? ''))),
  );

  const changeEngine = (engine: Engine) => {
    const next = normalizePickerValue(engines, { engine }, { lockEngine: true });
    if (next) onChange(next);
  };

  const changeModel = (modelValue: string) => {
    const model = models.find((m) => m.value === modelValue);
    if (!model) return;
    const effort = pickEffort(model, current.effort);
    onChange(effort ? { engine: current.engine, model: model.value, effort } : { engine: current.engine, model: model.value });
  };

  const changeEffort = (effort: string) => {
    onChange({ engine: current.engine, model: current.model, effort });
  };

  return (
    <div className={cn('space-y-2', className)} data-testid="engine-model-effort-picker">
      <div className={cn('grid gap-2', compact ? 'grid-cols-2 sm:grid-cols-3' : 'grid-cols-1 sm:grid-cols-3')}>
        {!lockEngine || !compact ? (
          <label className="block min-w-0 text-xs" htmlFor={`${baseId}-engine`}>
            <span className="mb-1 block font-medium text-admin-fg-muted">Engine</span>
            <select
              id={`${baseId}-engine`}
              className={selectClass}
              value={current.engine}
              disabled={disabled || lockEngine}
              onChange={(event) => changeEngine(event.target.value as Engine)}
            >
              {OWNER_AGENT_ENGINES.map((engine) => {
                const available = isEngineAvailable(engines[engine]);
                return (
                  <option key={engine} value={engine} disabled={!available && engine !== current.engine}>
                    {ENGINE_LABEL[engine]}
                    {available ? '' : ' (not signed in)'}
                  </option>
                );
              })}
            </select>
          </label>
        ) : null}
        <label className="block min-w-0 text-xs" htmlFor={`${baseId}-model`}>
          <span className="mb-1 block font-medium text-admin-fg-muted">Model</span>
          <select
            id={`${baseId}-model`}
            className={selectClass}
            value={current.model}
            disabled={disabled || models.length === 0}
            onChange={(event) => changeModel(event.target.value)}
          >
            {selectedModel ? null : <option value={current.model}>{current.model} (unavailable)</option>}
            {models.map((model) => (
              <option key={model.value} value={model.value} title={model.description}>
                {model.displayName || model.value}
              </option>
            ))}
          </select>
        </label>
        {efforts.length > 0 ? (
          <label className="block min-w-0 text-xs" htmlFor={`${baseId}-effort`}>
            <span className="mb-1 block font-medium text-admin-fg-muted">Effort</span>
            <select
              id={`${baseId}-effort`}
              className={selectClass}
              value={current.effort ?? ''}
              disabled={disabled}
              onChange={(event) => changeEffort(event.target.value)}
            >
              {efforts.map((effort) => (
                <option key={effort} value={effort}>
                  {effort}
                  {selectedModel?.defaultEffort === effort ? ' (default)' : ''}
                </option>
              ))}
            </select>
          </label>
        ) : null}
      </div>
      {switching ? (
        <p className="flex items-start gap-1.5 text-xs text-amber-700 dark:text-amber-300" role="status" data-testid="picker-switch-warning">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          Switching model or effort mid-session drops the prompt cache: the next turn re-reads the whole context and uses more quota.
        </p>
      ) : null}
    </div>
  );
}
