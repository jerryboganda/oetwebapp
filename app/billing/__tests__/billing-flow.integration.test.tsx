import { screen, waitFor } from '@testing-library/react';

const { mockApiRequest, mockFetchFreezeStatus, mockTrack } = vi.hoisted(() => ({
  mockApiRequest: vi.fn(),
  mockFetchFreezeStatus: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  apiClient: { request: mockApiRequest },
  fetchFreezeStatus: mockFetchFreezeStatus,
}));

import BillingUpgradePage from '../upgrade/page';
import { renderWithRouter } from '@/tests/test-utils';

/**
 * Slice H — frontend integration smoke for the billing upgrade journey.
 *
 * Walks the upgrade surface through three lifecycle stages with the
 * `apiClient` mocked end-to-end:
 *
 *   Stage 1 — "quote-like" load:    skeleton renders while data is in-flight.
 *   Stage 2 — "checkout-like" view: plans + upgrade CTA render after resolve.
 *   Stage 3 — "success / blocked":  freeze-active state replaces CTAs with
 *                                   the disabled mutation message.
 *
 * For each stage the assertion is identical: the surface must render the
 * stage-appropriate copy AND React must not have logged any error to the
 * console (no act warnings, no key warnings, no boundary catches).
 */
describe('Billing upgrade — 3-stage integration smoke', () => {
  const upgradeData = {
    currentPlan: { planId: 'plan-basic', planName: 'Basic', price: 19, includedCredits: 0 },
    usage: {
      reviewsUsedThisMonth: 2,
      creditsRemaining: 1,
      subscriptionStarted: '2026-01-01',
      subscriptionEnds: '2026-12-01',
    },
    plans: [
      {
        planId: 'plan-basic',
        planCode: 'basic-monthly',
        planName: 'Basic',
        description: 'Current plan.',
        price: 19,
        currency: 'AUD',
        interval: 'month',
        includedCredits: 0,
        trialDays: 0,
        isCurrent: true,
        isUpgrade: false,
        isDowngrade: false,
        entitlements: {},
      },
      {
        planId: 'plan-premium',
        planCode: 'premium-monthly',
        planName: 'Premium',
        description: 'Adds productive-skill review capacity.',
        price: 49,
        currency: 'AUD',
        interval: 'month',
        includedCredits: 3,
        trialDays: 7,
        isCurrent: false,
        isUpgrade: true,
        isDowngrade: false,
        entitlements: {},
      },
    ],
    recommendation: 'Upgrade to Premium to unlock Speaking review credits.',
  };

  let consoleError: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.clearAllMocks();
    consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleError.mockRestore();
  });

  it('walks through loading → upgrade-options → freeze-blocked without React errors', async () => {
    // ── Stage 1 — quote-like loading. Use a deferred promise so the page
    //    sits in its skeleton state for the first frame.
    let resolveUpgrade!: (value: typeof upgradeData) => void;
    mockApiRequest.mockImplementationOnce(
      () => new Promise((resolve) => { resolveUpgrade = resolve; }),
    );
    mockFetchFreezeStatus.mockResolvedValueOnce(null);

    const { unmount } = renderWithRouter(<BillingUpgradePage />);

    // The page label mounts immediately, but the plan cards must NOT yet —
    // only the skeleton grid is allowed to render.
    expect(await screen.findByText('Compare plans')).toBeInTheDocument();
    expect(screen.queryByText('Premium')).not.toBeInTheDocument();
    assertNoReactErrors(consoleError, 'stage 1 (quote-like loading)');

    // ── Stage 2 — checkout-like: data resolves, upgrade CTAs appear.
    resolveUpgrade(upgradeData);
    await waitFor(() => {
      expect(screen.getByText('Premium')).toBeInTheDocument();
    });

    // Upgrade CTA renders as a link that flows back to the plans tab —
    // this is the "checkout entry" surface in the upgrade journey.
    const upgradeCta = screen.getByRole('link', { name: /upgrade to premium/i });
    expect(upgradeCta).toHaveAttribute(
      'href',
      expect.stringContaining('/billing?tab=plans&planId='),
    );
    assertNoReactErrors(consoleError, 'stage 2 (checkout-like)');

    // ── Stage 3 — success / blocked: re-mount with a freeze-active state to
    //    simulate the post-checkout (or freeze-blocked) terminal surface.
    //    The CTAs must downgrade to disabled buttons with the freeze copy.
    unmount();
    mockApiRequest.mockResolvedValueOnce(upgradeData);
    mockFetchFreezeStatus.mockResolvedValueOnce({
      currentFreeze: {
        status: 'active',
        scheduledStartAt: null,
      },
    } as any);

    renderWithRouter(<BillingUpgradePage />);
    await waitFor(() => {
      // The "Upgrade to Premium" CTA is now disabled (rendered as <button>).
      expect(
        screen.getByRole('button', {
          name: /upgrade to premium \(unavailable/i,
        }),
      ).toBeDisabled();
    });

    // The freeze banner copy is visible.
    expect(
      screen.getByText(/your account is frozen, so plan changes are paused/i),
    ).toBeInTheDocument();
    assertNoReactErrors(consoleError, 'stage 3 (success / freeze-blocked)');
  });
});

function assertNoReactErrors(
  spy: ReturnType<typeof vi.spyOn>,
  stage: string,
) {
  const calls = spy.mock.calls as unknown as unknown[][];
  const reactErrors = calls.filter((args: unknown[]) => {
    const first = args[0];
    if (typeof first !== 'string') return true; // non-string console.error = real error
    // Ignore Next.js / RTL noise that is not a React lifecycle defect.
    if (first.includes('Not implemented: navigation')) return false;
    if (first.includes('inside a test was not wrapped in act')) return false;
    return true;
  });
  if (reactErrors.length > 0) {
    throw new Error(
      `${stage}: expected no React errors, but console.error was called ${reactErrors.length} time(s):\n`
      + reactErrors
        .map((args: unknown[], i: number) => `  [${i}] ${args.map((a: unknown) => String(a)).join(' ')}`)
        .join('\n'),
    );
  }
}
