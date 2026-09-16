import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi, afterEach } from 'vitest';
import { IosPurchaseGate } from './ios-purchase-gate';

vi.mock('@/lib/runtime-signals', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/runtime-signals')>();
  return {
    ...actual,
    getAppRuntimeKind: vi.fn(),
    getCapacitorPlatform: vi.fn(),
  };
});

const { getAppRuntimeKind, getCapacitorPlatform } = await import('@/lib/runtime-signals');

const mockedRuntime = vi.mocked(getAppRuntimeKind);
const mockedPlatform = vi.mocked(getCapacitorPlatform);

afterEach(() => {
  vi.clearAllMocks();
});

describe('IosPurchaseGate', () => {
  it('renders the purchase UI on web, desktop and Android shells', async () => {
    for (const [runtime, platform] of [
      ['web', null],
      ['desktop', null],
      ['capacitor-native', 'android'],
    ] as const) {
      mockedRuntime.mockReturnValue(runtime);
      mockedPlatform.mockReturnValue(platform);
      const { unmount } = render(
        <IosPurchaseGate>
          <button type="button">Buy now</button>
        </IosPurchaseGate>,
      );
      await waitFor(() => {
        expect(screen.getByRole('button', { name: 'Buy now' })).toBeVisible();
      });
      unmount();
    }
  });

  it('replaces the purchase UI with the enrol-on-website notice inside the iOS shell', async () => {
    mockedRuntime.mockReturnValue('capacitor-native');
    mockedPlatform.mockReturnValue('ios');
    render(
      <IosPurchaseGate>
        <button type="button">Buy now</button>
      </IosPurchaseGate>,
    );
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Enrol on our website' })).toBeVisible();
    });
    expect(screen.queryByRole('button', { name: 'Buy now' })).toBeNull();
    expect(screen.getByText(/happen securely on our website/i)).toBeVisible();
  });
});
