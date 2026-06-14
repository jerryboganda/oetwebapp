'use client';

import { useEffect, useState } from 'react';
import {
  PayPalScriptProvider,
  PayPalButtons,
  PayPalCardFieldsProvider,
  PayPalNameField,
  PayPalNumberField,
  PayPalExpiryField,
  PayPalCVVField,
  usePayPalCardFields,
} from '@paypal/react-paypal-js';
import { Loader2 } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  fetchPayPalClientConfig,
  captureBillingCheckout,
  type PayPalClientConfig,
  type PaymentCaptureResult,
} from '@/lib/api';

export interface PayPalExpandedCheckoutProps {
  /** Creates a PayPal order on the server and resolves its order id (the SDK `createOrder`). */
  createOrder: () => Promise<string>;
  /**
   * Captures the approved order id on the server (the SDK `onApprove`). Defaults to the
   * quote/wallet billing capture endpoint; pass a custom capturer for the cart or speaking surfaces.
   */
  capture?: (orderId: string) => Promise<PaymentCaptureResult>;
  /** Called after a successful, completed capture with the server result (status + redirectTo). */
  onCaptured: (result: PaymentCaptureResult) => void;
  /** Called on a hard error (order creation, capture, or SDK failure). */
  onError?: (message: string) => void;
  /** Called once when the embedded config is unavailable so the parent can fall back to the redirect flow. */
  onUnavailable?: () => void;
  /** Formatted total shown on the card-form submit button, e.g. "£24.00". */
  amountLabel: string;
  disabled?: boolean;
}

const GENERIC_START_ERROR = 'We could not start your payment. Please try again or choose another method.';
const GENERIC_CAPTURE_ERROR = 'Your payment could not be completed. Please try again or choose another method.';

/**
 * PayPal Expanded (embedded) checkout: PayPal/Venmo/Pay Later smart buttons plus, when the
 * account is eligible, on-page Advanced Card Fields — no redirect. The order is created and
 * captured server-side (`createOrder` / `capture`), which converges with the PayPal webhook
 * on the same idempotent fulfilment. Falls back to buttons-only when card fields are disabled
 * or ineligible, and signals `onUnavailable` when no client config is configured at all.
 */
export function PayPalExpandedCheckout({
  createOrder,
  capture,
  onCaptured,
  onError,
  onUnavailable,
  amountLabel,
  disabled = false,
}: PayPalExpandedCheckoutProps) {
  const [config, setConfig] = useState<PayPalClientConfig | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchPayPalClientConfig()
      .then((cfg) => {
        if (cancelled) return;
        setConfig(cfg);
        if (!cfg.enabled || !cfg.clientId) {
          onUnavailable?.();
        }
      })
      .catch(() => {
        if (cancelled) return;
        setConfig(null);
        onUnavailable?.();
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [onUnavailable]);

  const handleCreateOrder = async (): Promise<string> => {
    setError(null);
    setBusy(true);
    try {
      return await createOrder();
    } catch (e) {
      const msg = e instanceof Error && e.message ? e.message : GENERIC_START_ERROR;
      setError(msg);
      onError?.(msg);
      setBusy(false);
      throw e;
    }
  };

  const handleApprove = async (orderId?: string | null): Promise<void> => {
    if (!orderId) {
      setBusy(false);
      return;
    }
    try {
      const capturer = capture ?? captureBillingCheckout;
      const result = await capturer(orderId);
      if (result.status === 'completed') {
        onCaptured(result);
      } else {
        setError(GENERIC_CAPTURE_ERROR);
        onError?.(GENERIC_CAPTURE_ERROR);
      }
    } catch (e) {
      const msg = e instanceof Error && e.message ? e.message : GENERIC_CAPTURE_ERROR;
      setError(msg);
      onError?.(msg);
    } finally {
      setBusy(false);
    }
  };

  const handleSdkError = () => {
    setError(GENERIC_CAPTURE_ERROR);
    onError?.(GENERIC_CAPTURE_ERROR);
    setBusy(false);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center gap-2 rounded-2xl border border-border bg-surface p-6 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
        Loading secure payment…
      </div>
    );
  }

  if (!config || !config.enabled || !config.clientId) {
    return (
      <InlineAlert variant="warning">
        PayPal is not available right now. Please choose another payment method.
      </InlineAlert>
    );
  }

  const showCardFields = config.advancedCardsEnabled;

  return (
    <div className="space-y-4">
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <PayPalScriptProvider
        options={{
          clientId: config.clientId,
          currency: config.currency,
          intent: config.intent || 'capture',
          components: config.components || 'buttons,card-fields',
        }}
      >
        <PayPalButtons
          style={{ layout: 'vertical', shape: 'pill', label: 'pay' }}
          disabled={busy || disabled}
          createOrder={handleCreateOrder}
          onApprove={(data) => handleApprove(data.orderID)}
          onError={handleSdkError}
          onCancel={() => setBusy(false)}
        />

        {showCardFields ? (
          <div className="space-y-3">
            <div className="relative flex items-center justify-center">
              <span className="absolute inset-x-0 top-1/2 h-px bg-border" aria-hidden />
              <span className="relative bg-surface px-3 text-xs font-medium uppercase tracking-wide text-muted">
                or pay by card
              </span>
            </div>

            <PayPalCardFieldsProvider
              createOrder={handleCreateOrder}
              onApprove={(data) => handleApprove(data.orderID)}
              onError={handleSdkError}
            >
              <div className="space-y-3 rounded-2xl border border-border bg-surface p-4">
                <CardField label="Name on card">
                  <PayPalNameField />
                </CardField>
                <CardField label="Card number">
                  <PayPalNumberField />
                </CardField>
                <div className="grid grid-cols-2 gap-3">
                  <CardField label="Expiry">
                    <PayPalExpiryField />
                  </CardField>
                  <CardField label="CVV">
                    <PayPalCVVField />
                  </CardField>
                </div>
                <CardSubmitButton busy={busy || disabled} amountLabel={amountLabel} />
              </div>
            </PayPalCardFieldsProvider>
          </div>
        ) : null}
      </PayPalScriptProvider>
    </div>
  );
}

function CardField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-navy">{label}</span>
      <div className="rounded-lg border border-border bg-white px-1">{children}</div>
    </label>
  );
}

/** Submit button for the embedded card form. Must live inside <PayPalCardFieldsProvider>. */
function CardSubmitButton({ busy, amountLabel }: { busy: boolean; amountLabel: string }) {
  const { cardFieldsForm } = usePayPalCardFields();
  const [submitting, setSubmitting] = useState(false);

  const onSubmit = async () => {
    if (!cardFieldsForm) return;
    setSubmitting(true);
    try {
      // Submits the hosted card fields → triggers the provider's createOrder + onApprove.
      await cardFieldsForm.submit();
    } catch {
      // Validation/SDK errors surface through the provider's onError handler.
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Button
      type="button"
      fullWidth
      loading={busy || submitting}
      disabled={busy || submitting}
      onClick={onSubmit}
    >
      Pay {amountLabel}
    </Button>
  );
}
