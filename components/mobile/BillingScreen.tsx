'use client';

import { useCallback, useState } from 'react';
import { Globe2, ShieldCheck, Sparkles } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { openCustomerPortal } from '@/lib/native/billing-bridge';
import { PackageCard, type MobilePackageDescriptor } from './PackageCard';
import { useMobileBillingContext } from './use-mobile-billing-context';

interface BillingScreenProps {
  packages: readonly MobilePackageDescriptor[];
  /** Optional header shown above the package list. */
  heading?: string;
  /** Optional supporting copy shown under the heading. */
  subheading?: string;
}

/**
 * Mobile-first billing landing surface.
 *
 * - Shows a vertical stack of package cards (`PackageCard`).
 * - Includes a "Manage subscription on web" CTA that routes through the
 *   Stripe Customer Portal in the system browser.
 * - Includes an explanatory section about why purchases happen on the web
 *   (avoids app-store reviewer pushback about "directing users off platform"
 *   while staying truthful with the learner).
 */
export function BillingScreen({
  packages,
  heading = 'Choose your prep package',
  subheading = 'Purchases happen on our secure website so 100% of your spend supports your prep — no app store fees.',
}: BillingScreenProps) {
  const context = useMobileBillingContext();
  const [portalLoading, setPortalLoading] = useState(false);
  const [portalError, setPortalError] = useState<string | null>(null);

  const handleOpenPortal = useCallback(async () => {
    if (portalLoading) return;
    setPortalLoading(true);
    setPortalError(null);
    try {
      await openCustomerPortal();
    } catch (err) {
      setPortalError(
        err instanceof Error
          ? err.message
          : 'We could not open the subscription manager. Please try again from the web.',
      );
    } finally {
      setPortalLoading(false);
    }
  }, [portalLoading]);

  const isiOSGlobal = context?.route === 'web_only_cta';

  return (
    <div className="flex flex-col gap-5 px-4 pb-24 pt-4" data-testid="mobile-billing-screen">
      <header className="flex flex-col gap-2">
        <h1 className="text-xl font-bold text-navy">{heading}</h1>
        <p className="text-sm text-muted-foreground">{subheading}</p>
      </header>

      {isiOSGlobal ? (
        <InlineAlert variant="info" title="Manage on the web">
          To purchase or change your plan, visit oetwithdrhesham.co.uk on any browser. Existing
          subscriptions remain active across all your devices.
        </InlineAlert>
      ) : null}

      <section className="flex flex-col gap-3" aria-label="Available packages">
        {packages.map((pkg) => (
          <PackageCard key={pkg.productCode} package={pkg} contextOverride={context} />
        ))}
      </section>

      <section className="mt-2 rounded-2xl border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Globe2 className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-navy">Manage your subscription on the web</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Update payment methods, download invoices, change plans, or cancel any time from our
              secure customer portal.
            </p>
            <Button
              variant="secondary"
              size="md"
              className="mt-3"
              onClick={() => void handleOpenPortal()}
              loading={portalLoading}
            >
              Open subscription manager
            </Button>
            {portalError ? (
              <p role="alert" className="mt-2 text-xs text-red-600">
                {portalError}
              </p>
            ) : null}
          </div>
        </div>
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-emerald-100 text-emerald-700">
            <ShieldCheck className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-navy">Why purchases happen on the web</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              App stores charge 15–30% commission on in-app purchases. We process payments on our own
              website so every dollar funds your prep — not platform fees.
            </p>
          </div>
        </div>
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-amber-100 text-amber-700">
            <Sparkles className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-navy">Cross-device access</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Purchases made on the web unlock immediately in your mobile app — no separate
              subscription needed.
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}
