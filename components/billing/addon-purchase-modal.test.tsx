import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AddonPurchaseModal } from './addon-purchase-modal';
import { quoteAddonEligibility } from '@/lib/api';

const push = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push }),
}));

vi.mock('@/lib/api', () => ({
  quoteAddonEligibility: vi.fn(),
}));

const mockQuoteAddonEligibility = vi.mocked(quoteAddonEligibility);

describe('AddonPurchaseModal', () => {
  beforeEach(() => {
    push.mockReset();
    mockQuoteAddonEligibility.mockReset();
  });

  it('uses clean buyer-facing copy and exposes an accessible close button', async () => {
    const onClose = vi.fn();
    mockQuoteAddonEligibility.mockResolvedValue({
      eligible: true,
      addOnCode: 'speaking-1session',
      addOnName: 'Private Speaking session',
      addonKind: 'speaking_sessions',
      requiredFlag: 'speaking_addons',
      eligibleParents: [
        {
          subscriptionId: 'sub_123',
          planCode: 'speaking-crash',
          planName: 'Speaking Crash Course',
          expiresAt: '2026-12-31T00:00:00Z',
        },
      ],
      reason: null,
      redirectSku: null,
    });

    const { container } = render(
      <AddonPurchaseModal
        open
        addOnCode="speaking-1session"
        addOnLabel="Private Speaking session"
        addOnPriceGbp={18}
        onClose={onClose}
      />,
    );

    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument();
    expect(screen.getByText('£18')).toBeInTheDocument();
    expect(screen.getByText(/Checking eligibility/)).toBeInTheDocument();

    await screen.findByText('Will apply to:');
    expect(screen.getByText('Speaking Crash Course')).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/Â|â|�/);

    fireEvent.click(screen.getByRole('button', { name: /Continue to checkout/ }));
    expect(push).toHaveBeenCalledWith(
      '/checkout/review?productType=addon_purchase&priceId=speaking-1session&parentSubscriptionId=sub_123&quantity=1',
    );
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('redirects ineligible buyers to the eligible parent SKU', async () => {
    const onClose = vi.fn();
    mockQuoteAddonEligibility.mockResolvedValue({
      eligible: false,
      addOnCode: 'addon-3-letters',
      addOnName: 'Three letter pack',
      addonKind: 'writing_assessments',
      requiredFlag: 'writing_addons',
      eligibleParents: [],
      reason: 'no_eligible_parent',
      redirectSku: 'writing-crash',
    });

    render(
      <AddonPurchaseModal
        open
        addOnCode="addon-3-letters"
        addOnLabel="Three letter pack"
        addOnPriceGbp={30}
        onClose={onClose}
      />,
    );

    await screen.findByText('You need an eligible course first.');

    fireEvent.click(screen.getByRole('button', { name: /View the eligible course/ }));

    await waitFor(() => {
      expect(push).toHaveBeenCalledWith('/marketplace/packages/writing-crash');
      expect(onClose).toHaveBeenCalledTimes(1);
    });
  });

  it('sends an ineligible Tutor Book buyer straight to the £45 checkout, not a course upsell', async () => {
    const onClose = vi.fn();
    mockQuoteAddonEligibility.mockResolvedValue({
      eligible: false,
      addOnCode: 'tutor-book-addon',
      addOnName: 'The Tutor Book',
      addonKind: 'tutor_book',
      requiredFlag: 'tutor_book_discount',
      eligibleParents: [],
      reason: 'no_eligible_parent',
      redirectSku: 'crash-3letters',
    });

    render(
      <AddonPurchaseModal
        open
        addOnCode="tutor-book-addon"
        addOnLabel="The Tutor Book"
        addOnPriceGbp={32}
        onClose={onClose}
      />,
    );

    fireEvent.click(await screen.findByRole('button', { name: /Continue to checkout — £45/ }));

    expect(push).toHaveBeenCalledWith('/checkout/review?productType=plan_purchase&priceId=tutor-book&quantity=1');
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('You need an eligible course first.')).not.toBeInTheDocument();
  });

  it('tells a Tutor Book buyer who already owns it instead of charging again', async () => {
    mockQuoteAddonEligibility.mockResolvedValue({
      eligible: false,
      addOnCode: 'tutor-book-addon',
      addOnName: 'The Tutor Book',
      addonKind: 'tutor_book',
      requiredFlag: 'tutor_book_discount',
      eligibleParents: [],
      reason: 'addon_already_owned',
      redirectSku: null,
    });

    render(
      <AddonPurchaseModal
        open
        addOnCode="tutor-book-addon"
        addOnLabel="The Tutor Book"
        addOnPriceGbp={32}
        onClose={() => {}}
      />,
    );

    await screen.findByText('You already have The Tutor Book.');
    expect(screen.queryByRole('button', { name: /Continue to checkout/ })).not.toBeInTheDocument();
  });
});
