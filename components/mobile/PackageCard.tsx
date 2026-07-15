'use client';

import { useCallback, useState } from 'react';
import { CheckCircle2, ExternalLink, Globe2 } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Price } from '@/components/ui/price';
import { openExternalCheckout, type MobileBillingContext } from '@/lib/native/billing-bridge';
import { useMobileBillingContext } from './use-mobile-billing-context';

/**
 * Public shape mirrors the relevant subset of `BillingPlan` / `BillingAddOn`
 * so this card can render either packages or subscription tiers without
 * coupling to the legacy types.
 */
export interface MobilePackageDescriptor {
  productCode: string;
  /** Selects the express checkout product type. Defaults to a plan purchase. */
  kind?: 'plan' | 'addon';
  name: string;
  description?: string | null;
  /** Amount in major units (e.g. 19, 99.99). */
  priceAmount: number;
  currency: string;
  /** Optional list of bullet features shown under the price. */
  features?: readonly string[];
  /** Surfaces a "Recommended" pill. */
  isPopular?: boolean;
}

interface PackageCardProps {
  package: MobilePackageDescriptor;
  /** Allow callers to override the resolved billing context (mostly for tests / storybook). */
  contextOverride?: MobileBillingContext | null;
  className?: string;
}

export function PackageCard({ package: pkg, contextOverride, className }: PackageCardProps) {
  const resolved = useMobileBillingContext();
  const context = contextOverride ?? resolved;
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [redirecting, setRedirecting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const performExternalRedirect = useCallback(async () => {
    if (redirecting) return;
    setRedirecting(true);
    setError(null);
    try {
      await openExternalCheckout(pkg.productCode, {
        productType: pkg.kind === 'addon' ? 'addon_purchase' : 'plan_purchase',
      });
      setConfirmOpen(false);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : 'We could not open checkout. Please try again from the web.',
      );
    } finally {
      setRedirecting(false);
    }
  }, [pkg.productCode, pkg.kind, redirecting]);

  const handleClick = useCallback(() => {
    if (!context) return;
    if (context.route === 'external_browser') {
      void performExternalRedirect();
      return;
    }
    setConfirmOpen(true);
  }, [context, performExternalRedirect]);

  if (!context) {
    return (
      <Card padding="lg" className={className} aria-busy="true" data-testid="mobile-package-card-loading">
        <div className="h-32 animate-pulse rounded-lg bg-surface" />
      </Card>
    );
  }

  const isWeb = context.route === 'native_iap';
  const RouteIcon = isWeb ? null : context.allowExternalLink ? ExternalLink : Globe2;

  return (
    <>
      <Card
        padding="lg"
        hoverable
        className={className}
        data-testid="mobile-package-card"
        data-product-code={pkg.productCode}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-navy">{pkg.name}</h3>
            {pkg.description ? (
              <p className="mt-1 text-sm text-muted-foreground">{pkg.description}</p>
            ) : null}
          </div>
          {pkg.isPopular ? (
            <span className="shrink-0 rounded-full bg-primary/10 px-2 py-1 text-xs font-medium text-primary">
              Recommended
            </span>
          ) : null}
        </div>

        <div className="mt-4">
          <Price
            amount={pkg.priceAmount}
            currency={pkg.currency}
            className="text-2xl font-bold text-navy"
          />
        </div>

        {pkg.features?.length ? (
          <ul className="mt-3 space-y-1.5">
            {pkg.features.map((feature) => (
              <li key={feature} className="flex items-start gap-2 text-sm text-navy">
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : null}

        <Button
          variant="primary"
          size="md"
          fullWidth
          className="mt-4"
          onClick={handleClick}
          loading={redirecting}
        >
          {RouteIcon ? <RouteIcon className="h-4 w-4" aria-hidden="true" /> : null}
          {context.copy.ctaLabel}
        </Button>

        {error ? (
          <p role="alert" className="mt-2 text-xs text-red-600">
            {error}
          </p>
        ) : null}
      </Card>

      <Modal
        open={confirmOpen}
        onClose={() => {
          if (!redirecting) setConfirmOpen(false);
        }}
        title={context.copy.messageTitle}
        size="sm"
      >
        <p className="text-sm text-navy">{context.copy.messageBody}</p>
        {error ? (
          <p role="alert" className="mt-3 text-sm text-red-600">
            {error}
          </p>
        ) : null}
        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            variant="ghost"
            size="md"
            onClick={() => setConfirmOpen(false)}
            disabled={redirecting}
          >
            Not now
          </Button>
          {context.allowExternalLink ? (
            <Button
              variant="primary"
              size="md"
              onClick={() => void performExternalRedirect()}
              loading={redirecting}
            >
              {context.copy.ctaLabel}
            </Button>
          ) : (
            <Button
              variant="primary"
              size="md"
              onClick={() => void performExternalRedirect()}
              loading={redirecting}
            >
              Open in browser
            </Button>
          )}
        </div>
      </Modal>
    </>
  );
}
