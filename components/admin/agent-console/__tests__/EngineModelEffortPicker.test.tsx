import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
  EngineModelEffortPicker,
  normalizePickerValue,
  type EngineCapabilities,
} from '../EngineModelEffortPicker';
import type { EngineStatus } from '@/lib/owner-agent/types';

function engineStatus(engine: 'claude' | 'codex', overrides: Partial<EngineStatus> = {}): EngineStatus {
  return {
    engine,
    version: '1.0.0',
    auth: { state: 'signed_in' },
    models: [],
    rateLimits: null,
    ...overrides,
  };
}

// Opaque ids exactly as an engine might report them at runtime.
const engines: EngineCapabilities = {
  claude: engineStatus('claude', {
    models: [
      { value: 'model-alpha', displayName: 'Alpha', supportsEffort: true, efforts: ['low', 'medium', 'high'], defaultEffort: 'medium' },
      { value: 'model-beta', displayName: 'Beta', supportsEffort: false, efforts: [] },
    ],
  }),
  codex: engineStatus('codex', {
    auth: { state: 'signed_out' },
    models: [],
  }),
};

function optionsOf(select: HTMLElement): string[] {
  return within(select).getAllByRole('option').map((option) => option.textContent ?? '');
}

describe('EngineModelEffortPicker', () => {
  it('populates engines, models and efforts only from the reported capabilities', () => {
    render(
      <EngineModelEffortPicker engines={engines} value={{ engine: 'claude', model: 'model-alpha', effort: 'medium' }} onChange={vi.fn()} />,
    );

    const engine = screen.getByLabelText('Engine');
    expect(optionsOf(engine)).toEqual(['Claude Code', 'Codex (not signed in)']);
    expect((within(engine).getByRole('option', { name: 'Codex (not signed in)' }) as HTMLOptionElement).disabled).toBe(true);

    expect(optionsOf(screen.getByLabelText('Model'))).toEqual(['Alpha', 'Beta']);
    expect(optionsOf(screen.getByLabelText('Effort'))).toEqual(['low', 'medium (default)', 'high']);
  });

  it('hides effort for models that do not support it and drops the effort on switch', () => {
    const onChange = vi.fn();
    const { rerender } = render(
      <EngineModelEffortPicker engines={engines} value={{ engine: 'claude', model: 'model-alpha', effort: 'high' }} onChange={onChange} />,
    );

    fireEvent.change(screen.getByLabelText('Model'), { target: { value: 'model-beta' } });
    expect(onChange).toHaveBeenLastCalledWith({ engine: 'claude', model: 'model-beta' });

    rerender(<EngineModelEffortPicker engines={engines} value={{ engine: 'claude', model: 'model-beta' }} onChange={onChange} />);
    expect(screen.queryByLabelText('Effort')).not.toBeInTheDocument();
  });

  it('keeps a still-offered effort and falls back to the default otherwise', () => {
    expect(normalizePickerValue(engines, { engine: 'claude', model: 'model-alpha', effort: 'high' })).toEqual({
      engine: 'claude',
      model: 'model-alpha',
      effort: 'high',
    });
    expect(normalizePickerValue(engines, { engine: 'claude', model: 'model-alpha', effort: 'ultra' })).toEqual({
      engine: 'claude',
      model: 'model-alpha',
      effort: 'medium',
    });
    // Unknown model → first reported model; unavailable engine → first available engine.
    expect(normalizePickerValue(engines, { engine: 'codex', model: 'gone' })).toEqual({
      engine: 'claude',
      model: 'model-alpha',
      effort: 'medium',
    });
    expect(normalizePickerValue(null, { engine: 'claude' })).toBeNull();
  });

  it('warns about cache loss when switching model mid-session', () => {
    const { rerender } = render(
      <EngineModelEffortPicker
        engines={engines}
        value={{ engine: 'claude', model: 'model-alpha', effort: 'medium' }}
        sessionModel={{ model: 'model-alpha', effort: 'medium' }}
        onChange={vi.fn()}
        lockEngine
      />,
    );
    expect(screen.queryByTestId('picker-switch-warning')).not.toBeInTheDocument();

    rerender(
      <EngineModelEffortPicker
        engines={engines}
        value={{ engine: 'claude', model: 'model-alpha', effort: 'high' }}
        sessionModel={{ model: 'model-alpha', effort: 'medium' }}
        onChange={vi.fn()}
        lockEngine
      />,
    );
    expect(screen.getByTestId('picker-switch-warning')).toHaveTextContent(/prompt cache/i);
  });

  it('explains when no engine is signed in', () => {
    render(
      <EngineModelEffortPicker
        engines={{ claude: engineStatus('claude', { auth: { state: 'signed_out' } }), codex: engineStatus('codex', { auth: { state: 'signed_out' } }) }}
        value={null}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByTestId('picker-unavailable')).toHaveTextContent(/No engine is signed in/);
  });
});
