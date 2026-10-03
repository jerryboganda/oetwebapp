import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

// The dialog maps API errors through describeOwnerAgentError, which lives next
// to the shared client; the client itself is never called in these tests.
vi.mock('@/lib/api', () => ({ apiClient: { request: vi.fn() } }));

import { NewSessionDialog } from '../NewSessionDialog';
import type { EngineCapabilities } from '../EngineModelEffortPicker';
import type { EngineStatus } from '@/lib/owner-agent/types';

function engineStatus(engine: 'claude' | 'codex', overrides: Partial<EngineStatus> = {}): EngineStatus {
  return { engine, version: '1.0.0', auth: { state: 'signed_in' }, models: [], rateLimits: null, ...overrides };
}

const engines: EngineCapabilities = {
  claude: engineStatus('claude', {
    models: [{ value: 'model-alpha', displayName: 'Alpha', supportsEffort: false, efforts: [] }],
  }),
  codex: engineStatus('codex', { auth: { state: 'signed_out' }, models: [] }),
};

const VAGUE_COPY = 'The request is too vague to triage - add the task, files and expected result, then send again.';

describe('NewSessionDialog', () => {
  it('explains a vague first message from the triage conflict and keeps what was typed', async () => {
    const user = userEvent.setup();
    const conflict = Object.assign(
      new Error('Jev could not establish the task and impact. Clarify the request before continuing.'),
      { status: 409, code: 'jev_review_required' },
    );
    const onCreate = vi.fn().mockRejectedValue(conflict);
    render(<NewSessionDialog open engines={engines} onClose={vi.fn()} onCreate={onCreate} />);

    const message = await screen.findByLabelText('First message (optional)');
    // Not user.type: Modal's rAF focus move (first focusable = Close) would steal focus mid-typing.
    fireEvent.change(message, { target: { value: 'please look at the thing and fix it somehow' } });
    await user.click(screen.getByRole('button', { name: 'Start session' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(VAGUE_COPY);
    expect(screen.getByLabelText('First message (optional)')).toHaveValue('please look at the thing and fix it somehow');
    expect(screen.getByRole('button', { name: 'Start session' })).toBeEnabled();
    expect(onCreate).toHaveBeenCalledTimes(1);
    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({
      engine: 'claude',
      model: 'model-alpha',
      initialMessage: 'please look at the thing and fix it somehow',
    }));
  });

  it('keeps the generic text for other failures', async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockRejectedValue(Object.assign(new Error('Console is draining.'), { status: 423, code: 'owner_agent_draining' }));
    render(<NewSessionDialog open engines={engines} onClose={vi.fn()} onCreate={onCreate} />);

    await user.click(await screen.findByRole('button', { name: 'Start session' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Console is draining.');
  });
});
