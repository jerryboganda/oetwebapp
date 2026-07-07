import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchPayPalClientConfig, mockCaptureBillingCheckout } = vi.hoisted(() => ({
  mockFetchPayPalClientConfig: vi.fn(),
  mockCaptureBillingCheckout: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchPayPalClientConfig: mockFetchPayPalClientConfig,
  captureBillingCheckout: mockCaptureBillingCheckout,
}));

// The real PayPal SDK is heavy ESM with side effects; stub the pieces this
// component imports. PayPalButtons invokes onApprove when clicked so the
// capture path can be exercised deterministically. The `pp-create-order`
// button mirrors the real SDK's behavior when createOrder rejects: it awaits
// the prop, and on failure calls the SDK's own onError — the exact sequence
// that used to let handleSdkError clobber a more specific message.
vi.mock('@paypal/react-paypal-js', () => ({
  PayPalScriptProvider: ({ children }: { children?: React.ReactNode }) => <div data-testid="pp-script">{children}</div>,
  PayPalButtons: (props: {
    createOrder?: () => Promise<string>;
    onApprove?: (d: { orderID: string }) => void;
    onError?: (err: unknown) => void;
  }) => (
    <>
      <button type="button" data-testid="pp-approve" onClick={() => props.onApprove?.({ orderID: 'ORDER-1' })}>
        pay
      </button>
      <button
        type="button"
        data-testid="pp-create-order"
        onClick={async () => {
          try {
            await props.createOrder?.();
          } catch (e) {
            props.onError?.(e);
          }
        }}
      >
        create
      </button>
    </>
  ),
  PayPalCardFieldsProvider: ({ children }: { children?: React.ReactNode }) => <div>{children}</div>,
  PayPalNameField: () => <div />,
  PayPalNumberField: () => <div />,
  PayPalExpiryField: () => <div />,
  PayPalCVVField: () => <div />,
  usePayPalCardFields: () => ({ cardFieldsForm: null }),
}));

import { PayPalExpandedCheckout } from './paypal-expanded-checkout';

const enabledConfig = {
  enabled: true,
  clientId: 'sb-client',
  currency: 'GBP',
  intent: 'capture',
  components: 'buttons,card-fields',
  environment: 'sandbox',
  advancedCardsEnabled: false,
};

describe('PayPalExpandedCheckout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('signals onUnavailable and shows a warning when PayPal is dormant', async () => {
    mockFetchPayPalClientConfig.mockResolvedValue({ ...enabledConfig, enabled: false, clientId: null });
    const onUnavailable = vi.fn();

    render(
      <PayPalExpandedCheckout
        createOrder={vi.fn()}
        onCaptured={vi.fn()}
        onUnavailable={onUnavailable}
        amountLabel="£10.00"
      />,
    );

    await waitFor(() => expect(onUnavailable).toHaveBeenCalled());
    expect(screen.getByText(/PayPal is not available right now/i)).toBeTruthy();
  });

  it('signals onUnavailable when the config fetch fails', async () => {
    mockFetchPayPalClientConfig.mockRejectedValue(new Error('network'));
    const onUnavailable = vi.fn();

    render(
      <PayPalExpandedCheckout
        createOrder={vi.fn()}
        onCaptured={vi.fn()}
        onUnavailable={onUnavailable}
        amountLabel="£10.00"
      />,
    );

    await waitFor(() => expect(onUnavailable).toHaveBeenCalled());
  });

  it('surfaces the server failure reason when a capture is not completed', async () => {
    mockFetchPayPalClientConfig.mockResolvedValue(enabledConfig);
    mockCaptureBillingCheckout.mockResolvedValue({
      status: 'failed',
      orderId: 'ORDER-1',
      captureId: null,
      redirectTo: null,
      failureReason: 'payment_not_completed',
    });
    const onError = vi.fn();
    const onCaptured = vi.fn();

    render(
      <PayPalExpandedCheckout
        createOrder={vi.fn().mockResolvedValue('ORDER-1')}
        onCaptured={onCaptured}
        onError={onError}
        amountLabel="£10.00"
      />,
    );

    const approveButton = await screen.findByTestId('pp-approve');
    await userEvent.click(approveButton);

    await waitFor(() => expect(onError).toHaveBeenCalledWith(expect.stringMatching(/did not complete/i)));
    expect(onCaptured).not.toHaveBeenCalled();
    expect(screen.getByText(/did not complete/i)).toBeTruthy();
  });

  it('preserves the specific createOrder failure message when the SDK onError fires afterward', async () => {
    mockFetchPayPalClientConfig.mockResolvedValue(enabledConfig);
    const specificMessage = 'This payment method is temporarily unavailable. Please pay by card instead.';
    const onError = vi.fn();

    render(
      <PayPalExpandedCheckout
        createOrder={vi.fn().mockRejectedValue(new Error(specificMessage))}
        onCaptured={vi.fn()}
        onError={onError}
        amountLabel="£10.00"
      />,
    );

    // Mirrors the real PayPal SDK: it calls createOrder, and when that rejects,
    // it invokes its own onError (handleSdkError) right after. Before the fix,
    // handleSdkError unconditionally overwrote the specific message set by
    // handleCreateOrder with the generic "could not be completed" copy.
    const createOrderButton = await screen.findByTestId('pp-create-order');
    await userEvent.click(createOrderButton);

    await waitFor(() => expect(onError).toHaveBeenCalledWith(specificMessage));
    expect(onError).not.toHaveBeenCalledWith(expect.stringMatching(/could not be completed/i));
    expect(screen.getByText(specificMessage)).toBeTruthy();
  });
});
