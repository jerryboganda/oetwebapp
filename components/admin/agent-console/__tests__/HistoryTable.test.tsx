import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SessionSummary } from '@/lib/owner-agent/types';

const { listSessions, push } = vi.hoisted(() => ({ listSessions: vi.fn(), push: vi.fn() }));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push }),
}));

vi.mock('@/lib/owner-agent/api', () => ({
  listSessions: (...args: unknown[]) => listSessions(...args),
  listOwners: async () => [{ accountId: 'auth_admin_local_001', email: 'admin@oet-prep.dev' }],
  describeOwnerAgentError: (_error: unknown, fallback: string) => fallback,
}));

import { HistoryTable, SessionHistory, formatSessionCost, formatStartedBy } from '../HistoryTable';

const SESSION_A = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';
const SESSION_B = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2F';

function summary(overrides: Partial<SessionSummary> = {}): SessionSummary {
  return {
    id: SESSION_A,
    title: 'Guarded T3 run',
    engine: 'claude',
    model: 'opaque-model',
    mode: 'guarded',
    status: 'archived',
    branch: 'agent/t3',
    tainted: false,
    createdAt: '2026-09-27T09:00:00.000Z',
    updatedAt: '2026-09-27T10:00:00.000Z',
    lastSeq: 42,
    usage: { inputTokens: 10, outputTokens: 20, costUsd: 1.234 },
    createdBy: '3f2a9c1e-7b44-4d2a-9d6e-0a1b2c3d4e5f',
    firstMessage: 'Please run the guarded T3 test',
    ...overrides,
  };
}

async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('SessionHistory filters', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    listSessions.mockReset();
    listSessions.mockResolvedValue([summary()]);
    push.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows archived sessions by default and maps search (debounced), engine, status and archived to list params', async () => {
    render(<SessionHistory enabled />);
    await flush();
    expect(listSessions).toHaveBeenCalledTimes(1);
    expect(listSessions).toHaveBeenLastCalledWith({ includeArchived: true, limit: 50 });
    expect(screen.getByLabelText('Show archived')).toBeChecked();

    fireEvent.change(screen.getByLabelText('Search'), { target: { value: 'T3' } });
    await flush(100);
    // Still debouncing: no request per keystroke.
    expect(listSessions).toHaveBeenCalledTimes(1);
    await flush(300);
    expect(listSessions).toHaveBeenCalledTimes(2);
    expect(listSessions).toHaveBeenLastCalledWith({ q: 'T3', includeArchived: true, limit: 50 });

    fireEvent.change(screen.getByLabelText('Engine'), { target: { value: 'codex' } });
    await flush();
    expect(listSessions).toHaveBeenLastCalledWith({ q: 'T3', engine: 'codex', includeArchived: true, limit: 50 });

    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'error' } });
    await flush();
    expect(listSessions).toHaveBeenLastCalledWith({ q: 'T3', engine: 'codex', status: 'error', includeArchived: true, limit: 50 });

    fireEvent.click(screen.getByLabelText('Show archived'));
    await flush();
    expect(listSessions).toHaveBeenLastCalledWith({ q: 'T3', engine: 'codex', status: 'error', includeArchived: false, limit: 50 });
  });

  it('loads the next page with before = the last row updatedAt', async () => {
    listSessions.mockReset();
    listSessions
      .mockResolvedValueOnce([summary()])
      .mockResolvedValueOnce([summary({ id: SESSION_B, title: 'Older run', updatedAt: '2026-09-26T08:00:00.000Z' })]);
    render(<SessionHistory enabled pageSize={1} />);
    await flush();

    fireEvent.click(screen.getByRole('button', { name: /load more/i }));
    await flush();
    expect(listSessions).toHaveBeenLastCalledWith({ includeArchived: true, limit: 1, before: '2026-09-27T10:00:00.000Z' });
    expect(screen.getAllByTestId('history-row')).toHaveLength(2);
  });

  it('does not query while the console is locked', async () => {
    render(<SessionHistory enabled={false} />);
    await flush(1_000);
    expect(listSessions).not.toHaveBeenCalled();
  });
});

describe('HistoryTable', () => {
  beforeEach(() => {
    push.mockReset();
  });

  it('renders title, first message, started by, cost and links each row to the transcript replay', () => {
    render(<HistoryTable rows={[summary()]} />);

    const row = screen.getByTestId('history-row');
    const link = within(row).getByRole('link', { name: 'Guarded T3 run' });
    expect(link).toHaveAttribute('href', `/admin/agent-console/${SESSION_A}`);
    expect(within(row).getByText('Please run the guarded T3 test')).toBeInTheDocument();
    expect(within(row).getByText('Account 3f2a9c1e…')).toBeInTheDocument();
    expect(within(row).getByText('$1.23')).toBeInTheDocument();
    expect(within(row).getByText('Archived')).toBeInTheDocument();

    // Clicking anywhere on the row opens the session.
    fireEvent.click(within(row).getByText('$1.23'));
    expect(push).toHaveBeenCalledWith(`/admin/agent-console/${SESSION_A}`);
  });

  it('distinguishes "no match" from "no sessions"', () => {
    const { rerender } = render(<HistoryTable rows={[]} filtered />);
    expect(screen.getByText('No sessions match')).toBeInTheDocument();
    rerender(<HistoryTable rows={[]} />);
    expect(screen.getByText('No sessions yet')).toBeInTheDocument();
  });

  it('formats started-by and cost defensively', () => {
    expect(formatStartedBy(null).label).toBe('—');
    expect(formatStartedBy('AUTH_ADMIN_LOCAL_001', new Map([['auth_admin_local_001', 'admin@oet-prep.dev']])).label).toBe('admin@oet-prep.dev');
    expect(formatStartedBy('short-id').label).toBe('Account short-id');
    expect(formatSessionCost(undefined)).toBe('—');
    expect(formatSessionCost(0)).toBe('$0.00');
    expect(formatSessionCost(0.004)).toBe('<$0.01');
  });
});
