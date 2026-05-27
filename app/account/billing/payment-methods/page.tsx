'use client';

import Link from 'next/link';
import { ArrowLeft, CreditCard, ShieldCheck } from 'lucide-react';

import { BillingPortalLauncher } from '@/components/billing/BillingPortalLauncher';

/**
 * Payment-methods landing. We never store card details on our side -
 * Stripe Customer Portal handles the full add/remove/set-default flow.
 * This page just explains that and launches the portal.
 */
export default function AccountPaymentMethodsPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6 px-4 py-10">
      <header>
        <Link
          href="/account/billing"
          className="inline-flex items-center gap-1 text-xs text-muted hover:text-navy"
        >
          <ArrowLeft className="h-3.5 w-3.5" /> Billing overview
        </Link>
        <h1 className="mt-2 text-3xl font-bold text-navy">Payment methods</h1>
        <p className="mt-1 text-sm text-muted">
          Cards, wallets, and bank debits are stored directly with Stripe. Open the secure portal
          to add a new payment method, set a default, or remove old cards.
        </p>
      </header>

      <div className="rounded-2xl border border-border bg-background p-6 shadow-sm">
        <div className="flex items-start gap-3">
          <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
            <CreditCard className="h-5 w-5" aria-hidden="true" />
          </span>
          <div>
            <h2 className="text-lg font-semibold text-navy">Manage payment methods on Stripe</h2>
            <p className="mt-1 text-sm text-muted">
              Clicking below opens the Stripe Customer Portal in this window. You can return at
              any time via the navigation menu.
            </p>
            <BillingPortalLauncher className="mt-4">Open Stripe portal</BillingPortalLauncher>
          </div>
        </div>
      </div>

      <div className="rounded-2xl border border-border bg-surface p-5 text-sm text-muted">
        <div className="flex items-start gap-2">
          <ShieldCheck className="mt-0.5 h-4 w-4 flex-none text-emerald-700" aria-hidden="true" />
          <p>
            All payments are encrypted in transit and tokenised by Stripe. We never see your full
            card number or CVV. For receipts and past charges, see your{' '}
            <Link href="/account/billing/invoices" className="text-primary hover:underline">
              invoice history
            </Link>
            .
          </p>
        </div>
      </div>
    </div>
  );
}
