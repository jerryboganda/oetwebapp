'use client';

import { Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Clock } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { SendProofOnWhatsAppButton } from '@/components/billing/send-proof-whatsapp-button';
import { CheckoutSuccessPoller } from '@/components/checkout';

/**
 * Post-payment confirmation. Two routes land here:
 *  - hosted card checkout, which returns `session_id` and is polled until the
 *    backend confirms fulfilment;
 *  - the embedded PayPal capture on /checkout/review, which redirects here with its
 *    `order` id — the same checkout session id the capture was created against — so
 *    it is polled the same way.
 *
 * The verdict (paid, manual fulfilment, failure) always comes from the backend poll;
 * URL parameters only ever supply the lookup reference and display wording.
 * Both end with the proof-of-payment WhatsApp CTA (spec 2026-07-15 §7).
 */
export default function CheckoutSuccessPage() {
  return (
    <Suspense fallback={null}>
      <CheckoutSuccessContent />
    </Suspense>
  );
}

function CheckoutSuccessContent() {
  const searchParams = useSearchParams();
  const sessionId = searchParams?.get('session_id') ?? '';
  const course = searchParams?.get('course') ?? '';
  const amountParam = searchParams?.get('amount');
  const currency = searchParams?.get('currency') ?? '';
  const reference = searchParams?.get('order') ?? searchParams?.get('quote') ?? '';
  const pollReference = sessionId || reference;
  const amount = amountParam != null && amountParam !== '' ? Number(amountParam) : null;
  // AI / practice / mock packages (Products 30-47) activate instantly and
  // never use the proof-of-payment verification route.
  const isAiPackage =
    reference.startsWith('pkg_') ||
    (searchParams?.get('addons') ?? '').split(',').filter(Boolean).every((code) => code.startsWith('pkg_'))
      && Boolean(searchParams?.get('addons'));

  return (
    <div className="min-h-screen bg-background-light text-navy">
      <div className="mx-auto max-w-3xl space-y-6 px-4 py-12">
        <header>
          <h1 className="text-3xl font-bold">Thank you</h1>
          <p className="mt-1 text-sm text-muted">
            We are confirming your purchase with the payment processor.
          </p>
        </header>

        {pollReference ? (
          <CheckoutSuccessPoller sessionId={pollReference} />
        ) : (
          <ConfirmingPurchase course={course} />
        )}

        {!isAiPackage && (
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="text-sm font-bold text-navy">Need to send us your receipt?</h2>
            <p className="mt-1 text-xs leading-5 text-muted">
              Message us on WhatsApp with your proof of payment and we&apos;ll pick it up from there.
            </p>
            <SendProofOnWhatsAppButton
              className="mt-3"
              course={course}
              amount={amount}
              currency={currency}
              reference={reference}
            />
          </div>
        )}
      </div>
    </div>
  );
}

function ConfirmingPurchase({ course }: { course: string }) {
  return (
    <div className="space-y-6">
      <div className="rounded-2xl border border-border bg-surface p-6">
        <div className="flex items-start gap-3">
          <Clock className="mt-0.5 h-6 w-6 flex-none text-muted" aria-hidden="true" />
          <div>
            <h2 className="text-lg font-semibold text-navy">Confirming your purchase</h2>
            {course ? <p className="mt-1 text-sm font-medium text-navy">{course}</p> : null}
            <p className="mt-2 text-sm leading-6 text-navy">
              We could not match this page to a checkout on your account. Your purchase is confirmed
              once the payment processor reports it — check Billing for the latest status.
            </p>
          </div>
        </div>
      </div>
      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link href="/billing">Check Billing</Link>
        </Button>
        <Button asChild variant="outline">
          <Link href="/dashboard">Back to dashboard</Link>
        </Button>
      </div>
    </div>
  );
}
