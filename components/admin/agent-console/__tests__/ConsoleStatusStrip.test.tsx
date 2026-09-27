import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ConsoleStatusStrip, formatUnlockRemaining } from '../ConsoleStatusStrip';

describe('ConsoleStatusStrip unlock status', () => {
  it('formats the remaining unlock time in whole minutes, rounding up', () => {
    const now = Date.UTC(2026, 8, 28, 9, 0, 0);
    expect(formatUnlockRemaining(new Date(now + 42 * 60_000).toISOString(), now)).toBe('Unlocked · 42 min left');
    expect(formatUnlockRemaining(new Date(now + 41 * 60_000 + 1).toISOString(), now)).toBe('Unlocked · 42 min left');
    expect(formatUnlockRemaining(new Date(now - 1_000).toISOString(), now)).toBe('Unlocked · 0 min left');
    expect(formatUnlockRemaining(null, now)).toBe('Unlocked');
  });

  it('shows the time left and locks on "Lock now"', async () => {
    const onLockNow = vi.fn(async () => undefined);
    render(
      <ConsoleStatusStrip
        status={null}
        unlockExpiresAt={new Date(Date.now() + 30 * 60_000 - 1_000).toISOString()}
        onLockNow={onLockNow}
        onKillSwitch={vi.fn()}
        onApplyUpdate={vi.fn()}
      />,
    );
    expect(screen.getByTestId('unlock-status')).toHaveTextContent('Unlocked · 30 min left');
    fireEvent.click(screen.getByRole('button', { name: /lock now/i }));
    await waitFor(() => expect(onLockNow).toHaveBeenCalledTimes(1));
  });
});
