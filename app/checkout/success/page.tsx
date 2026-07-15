'use client';

import { Suspense } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { CheckCircle2, Clock } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { SendProofOnWhatsAppButton } from '@/components/billing/send-proof-whatsapp-button';
import { CheckoutSuccessPoller } from '@/components/checkout';

/**
 * Post-payment confirmation. Two routes land here:
 *  - hosted card checkout, which returns `session_id` and is polled until the
 *    backend confirms fulfilment;
 *  - the embedded PayPal capture on /checkout/review, which redirects here with the
 *    order context it already holds (there is no session to poll — the capture call
 *    itself confirmed the payment).
 *
 * Both end with the proof-of-payment WhatsApp CTA (spec 2026-07-15 §7).
 */
export default function CheckoutSuccessPage() {
  return (
    <Suspense fallback={null}>
      <CheckoutSuccessContent />
    </Suspense>
  );
}

/** Delivery methods where payment does NOT release access — an admin hands the
 *  package over and only then does the subscription go Active (spec §2/§5). */
const MANUAL_DELIVERY = new Set(['manual_web', 'telegram', 'manual_material']);

function CheckoutSuccessContent() {
  const searchParams = useSearchParams();
  const sessionId = searchParams?.get('session_id') ?? '';
  const course = searchParams?.get('course') ?? '';
  const amountParam = searchParams?.get('amount');
  const currency = searchParams?.get('currency') ?? '';
  const reference = searchParams?.get('order') ?? searchParams?.get('quote') ?? '';
  const delivery = searchParams?.get('delivery') ?? '';
  const amount = amountParam != null && amountParam !== '' ? Number(amountParam) : null;
  const isManualDelivery = MANUAL_DELIVERY.has(delivery);

  return (
    <div className="min-h-screen bg-background-light text-navy">
      <div className="mx-auto max-w-3xl space-y-6 px-4 py-12">
        <header>
          <h1 className="text-3xl font-bold">Thank you</h1>
          <p className="mt-1 text-sm text-muted">
            {isManualDelivery
              ? 'We have your payment. Your package is handed over by our team — details below.'
              : 'We are confirming your purchase with the payment processor.'}
          </p>
        </header>

        {isManualDelivery ? (
          <PendingManualFulfilment delivery={delivery} course={course} />
        ) : sessionId ? (
          <CheckoutSuccessPoller sessionId={sessionId} />
        ) : (
          <PaymentReceived course={course} />
        )}

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
      </div>
    </div>
  );
}

/**
 * A paid order whose subscription stays Pending until an admin marks it fulfilled.
 * Must not imply access is live — it is not, and the entitlement resolver grants
 * nothing for a Pending subscription.
 */
function PendingManualFulfilment({ delivery, course }: { delivery: string; course: string }) {
  const handover =
    delivery === 'telegram'
      ? 'Once our team verifies your payment we will send you the Telegram invite link — it appears on your billing page and is emailed to you.'
      : delivery === 'manual_material'
        ? 'Once our team verifies your payment we will arrange delivery of your materials and confirm the details with you.'
        : 'Once our team verifies your payment we will switch your access on and confirm it with you.';

  return (
    <div className="space-y-6">
      <div className="rounded-2xl border border-warning/30 bg-warning/10 p-6">
        <div className="flex items-start gap-3">
          <Clock className="mt-0.5 h-6 w-6 flex-none text-warning" aria-hidden="true" />
          <div>
            <h2 className="text-lg font-semibold text-warning">Pending manual fulfilment</h2>
            {course ? <p className="mt-1 text-sm font-medium text-navy">{course}</p> : null}
            <p className="mt-2 text-sm leading-6 text-navy">
              Your payment is in. This package is not activated automatically — an admin verifies your
              proof of payment and hands it over. {handover}
            </p>
            <p className="mt-2 text-sm leading-6 text-navy">
              Your access is <strong>not live yet</strong>, so don&apos;t worry if the package still looks
              locked on your dashboard.
            </p>
          </div>
        </div>
      </div>
      <div className="flex flex-wrap gap-3">
        <Button asChild variant="outline">
          <Link href="/billing">Track this order in Billing</Link>
        </Button>
      </div>
    </div>
  );
}

/** Embedded PayPal capture: the payment is already confirmed, there is nothing to poll. */
function PaymentReceived({ course }: { course: string }) {
  return (
    <div className="space-y-6">
      <div className="rounded-2xl border border-success/30 bg-success/10 p-6">
        <div className="flex items-start gap-3">
          <CheckCircle2 className="mt-0.5 h-6 w-6 flex-none text-success" aria-hidden="true" />
          <div>
            <h2 className="text-lg font-semibold text-success">Payment received</h2>
            {course ? <p className="mt-1 text-sm font-medium text-navy">{course}</p> : null}
            <p className="mt-2 text-sm text-navy">
              Thanks for your purchase. We have added the new entitlements to your account.
            </p>
          </div>
        </div>
      </div>
      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link href="/dashboard">Go to my dashboard</Link>
        </Button>
        <Button asChild variant="outline">
          <Link href="/billing">View billing</Link>
        </Button>
      </div>
    </div>
  );
}
