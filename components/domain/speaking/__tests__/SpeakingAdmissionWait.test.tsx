/**
 * Vitest spec for `SpeakingAdmissionWait` (live AI Speaking admission queue, owner decision 5 Oct 2026).
 *
 * Pins the load-bearing behaviours:
 *   1. It shows the learner's place, the line length and a plain-words wait estimate, and says that no timer
 *      runs and no credit is used while waiting.
 *   2. It repeats the start call every `pollAfterSeconds` while the tab is visible, never overlapping attempts.
 *   3. It is visibility-aware: slow while hidden, an immediate attempt when the tab becomes visible again.
 *   4. It stops when unmounted (the parent unmounts it once admitted) and survives a failed attempt.
 */
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SpeakingLiveAdmission } from '@/lib/api/speaking-admission';
import { HIDDEN_RETRY_MS, SpeakingAdmissionWait } from '../SpeakingAdmissionWait';

const waiting = (overrides: Partial<SpeakingLiveAdmission> = {}): SpeakingLiveAdmission => ({
  status: 'waiting',
  position: 3,
  queueLength: 7,
  estimatedWaitSeconds: 190,
  pollAfterSeconds: 4,
  ...overrides,
});

function setVisibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
}

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

describe('SpeakingAdmissionWait', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    setVisibility('visible');
  });

  afterEach(() => {
    vi.useRealTimers();
    setVisibility('visible');
  });

  it('shows the place, the line, a wait estimate in words, and that nothing is timed or charged', () => {
    render(<SpeakingAdmissionWait admission={waiting()} subject="practice" onAttempt={vi.fn()} />);

    const position = screen.getByTestId('speaking-admission-position');
    expect(position).toHaveTextContent('Position 3');
    expect(position).toHaveTextContent('of 7');
    expect(position).toHaveTextContent('Estimated wait: about 4 minutes');
    expect(screen.getByText(/no timer is running and no credit is used/i)).toBeInTheDocument();
    expect(screen.getByText(/your role-play starts as soon as a place is free/i)).toBeInTheDocument();
  });

  it('words the wait for an exam as an exam', () => {
    render(<SpeakingAdmissionWait admission={waiting()} subject="exam" onAttempt={vi.fn()} />);

    expect(screen.getByText(/your exam starts as soon as a place is free/i)).toBeInTheDocument();
  });

  it('repeats the start call every pollAfterSeconds while the tab is visible', async () => {
    const onAttempt = vi.fn().mockResolvedValue(undefined);
    render(<SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 4 })} subject="exam" onAttempt={onAttempt} />);

    await advance(3_999);
    expect(onAttempt).not.toHaveBeenCalled();

    await advance(1);
    expect(onAttempt).toHaveBeenCalledTimes(1);

    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(2);
  });

  it('never starts a second attempt while one is still in flight', async () => {
    let release: () => void = () => undefined;
    const onAttempt = vi.fn(
      () => new Promise<void>((resolve) => {
        release = resolve;
      }),
    );
    render(<SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 4 })} subject="exam" onAttempt={onAttempt} />);

    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);

    // The first attempt is still pending: however long it takes, no second one starts.
    await advance(60_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);

    await act(async () => {
      release();
    });
    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(2);
  });

  it('slows to the hidden interval while the tab is hidden', async () => {
    const onAttempt = vi.fn().mockResolvedValue(undefined);
    render(<SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 4 })} subject="exam" onAttempt={onAttempt} />);

    setVisibility('hidden');
    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);

    // The next attempt is scheduled with the hidden interval, not the visible one.
    await advance(HIDDEN_RETRY_MS - 1);
    expect(onAttempt).toHaveBeenCalledTimes(1);
    await advance(1);
    expect(onAttempt).toHaveBeenCalledTimes(2);
  });

  it('attempts at once when the tab becomes visible again', async () => {
    const onAttempt = vi.fn().mockResolvedValue(undefined);
    render(<SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 30 })} subject="exam" onAttempt={onAttempt} />);

    setVisibility('hidden');
    await advance(1_000);
    expect(onAttempt).not.toHaveBeenCalled();

    await act(async () => {
      setVisibility('visible');
    });
    expect(onAttempt).toHaveBeenCalledTimes(1);
  });

  it('keeps retrying after a failed attempt', async () => {
    const onAttempt = vi.fn().mockRejectedValueOnce(new Error('network')).mockResolvedValue(undefined);
    render(<SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 4 })} subject="exam" onAttempt={onAttempt} />);

    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);
    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(2);
  });

  it('stops retrying once unmounted (the parent unmounts it when admitted)', async () => {
    const onAttempt = vi.fn().mockResolvedValue(undefined);
    const { unmount } = render(
      <SpeakingAdmissionWait admission={waiting({ pollAfterSeconds: 4 })} subject="exam" onAttempt={onAttempt} />,
    );

    await advance(4_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);

    unmount();
    await advance(120_000);
    expect(onAttempt).toHaveBeenCalledTimes(1);
  });

  it('offers to leave the queue only when the parent supplies a way to', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const { rerender } = render(<SpeakingAdmissionWait admission={waiting()} subject="exam" onAttempt={vi.fn()} />);
    expect(screen.queryByRole('button', { name: /leave the queue/i })).not.toBeInTheDocument();

    const onLeave = vi.fn();
    rerender(<SpeakingAdmissionWait admission={waiting()} subject="exam" onAttempt={vi.fn()} onLeave={onLeave} />);
    await user.click(screen.getByRole('button', { name: /leave the queue/i }));
    expect(onLeave).toHaveBeenCalledTimes(1);
  });
});
