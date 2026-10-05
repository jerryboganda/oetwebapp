import React, { Suspense, type ComponentType, type ReactNode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';

// next/dynamic is React.lazy under Suspense: a rejected import() is thrown from render, which is
// what a chunk that cannot be downloaded does in the app.
vi.mock('next/dynamic', () => ({
  default: (
    loader: () => Promise<ComponentType<Record<string, unknown>>>,
    options?: { loading?: () => ReactNode },
  ) => {
    const Lazy = React.lazy(() => loader().then((component) => ({ default: component })));
    return function MockDynamic(props: Record<string, unknown>) {
      return (
        <Suspense fallback={options?.loading?.() ?? null}>
          <Lazy {...props} />
        </Suspense>
      );
    };
  },
}));

// Reading the component off the module throws, the way a failed chunk download rejects the import.
vi.mock('./whop-embedded-checkout', () => ({
  get WhopEmbeddedCheckout() {
    throw new Error('Loading chunk 42 failed.');
  },
}));
vi.mock('@/components/billing/paypal-expanded-checkout', () => ({
  get PayPalExpandedCheckout() {
    throw new Error('Loading chunk 43 failed.');
  },
}));

import { LazyWhopEmbeddedCheckout } from './lazy-whop-embedded-checkout';
import { LazyPayPalExpandedCheckout } from '@/components/billing/lazy-paypal-expanded-checkout';
import { CheckoutLoadBoundary } from './checkout-load-boundary';

function Boom(): never {
  throw new Error('render failed');
}

describe('lazy checkout components when their code cannot be loaded', () => {
  beforeEach(() => {
    // React reports an error a boundary caught on the console.
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('Whop: takes the hosted-checkout fallback once instead of throwing to an error page', async () => {
    const onUnavailable = vi.fn();
    const { container } = render(
      <LazyWhopEmbeddedCheckout
        planId="plan_1"
        checkoutUrl="https://whop.example.test/checkout"
        returnUrl="https://app.example.test/billing/payment-return"
        sessionId="ch_1"
        onComplete={vi.fn()}
        onUnavailable={onUnavailable}
      />,
    );

    await waitFor(() => expect(onUnavailable).toHaveBeenCalledTimes(1));
    expect(container).toBeEmptyDOMElement();
  });

  it('PayPal: tells the page it is unavailable once so the page can use the redirect flow', async () => {
    const onUnavailable = vi.fn();
    const { container } = render(
      <LazyPayPalExpandedCheckout
        createOrder={() => Promise.resolve('order-1')}
        onCaptured={vi.fn()}
        onUnavailable={onUnavailable}
        amountLabel="£24.00"
      />,
    );

    await waitFor(() => expect(onUnavailable).toHaveBeenCalledTimes(1));
    expect(container).toBeEmptyDOMElement();
  });

  it('PayPal: a page that supplies no onUnavailable still gets a quiet failure, not a thrown error', async () => {
    const { container } = render(
      <LazyPayPalExpandedCheckout
        createOrder={() => Promise.resolve('order-1')}
        onCaptured={vi.fn()}
        amountLabel="£24.00"
      />,
    );

    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });
});

describe('CheckoutLoadBoundary', () => {
  it('renders its children when nothing fails', () => {
    const onFailed = vi.fn();
    render(
      <CheckoutLoadBoundary onFailed={onFailed}>
        <p>Pay now</p>
      </CheckoutLoadBoundary>,
    );

    expect(screen.getByText('Pay now')).toBeInTheDocument();
    expect(onFailed).not.toHaveBeenCalled();
  });

  it('reports a render failure once and shows nothing in its place', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const onFailed = vi.fn();
    const { container } = render(
      <CheckoutLoadBoundary onFailed={onFailed}>
        <Boom />
      </CheckoutLoadBoundary>,
    );

    expect(onFailed).toHaveBeenCalledTimes(1);
    expect(container).toBeEmptyDOMElement();
    vi.restoreAllMocks();
  });
});
