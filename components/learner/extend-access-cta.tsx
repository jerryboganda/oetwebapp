'use client';

import { useMemo, useState } from 'react';
import { CalendarClock, Clock } from 'lucide-react';
import { AddonPurchaseModal } from '@/components/billing/addon-purchase-modal';

/** Code of the seeded access-extension add-on (90 days). */
const EXTEND_ACCESS_ADDON_CODE = 'addon-extend-90';

/** Show the CTA when access expires within this many days (or has already lapsed). */
const EXPIRY_WINDOW_DAYS = 14;

interface ExtendAccessCtaProps {
  /** True when the learner currently holds an eligible course subscription. */
  hasEligibleSubscription?: boolean;
  /** ISO expiry of the active subscription, from the entitlement snapshot. */
  expiresAt?: string | null;
}

/**
 * Extend Access CTA — surfaces a single "Extend access" entry point on the
 * dashboard when the learner's course access has lapsed or is within
 * {@link EXPIRY_WINDOW_DAYS} of expiring. Clicking it opens the existing
 * {@link AddonPurchaseModal} for the seeded `addon-extend-90` add-on, which
 * resolves eligibility server-side via `/v1/billing/quote/addon`
 * (AddonEligibilityService gates `access_extension` on the parent plan's
 * `ExtensionAllowed` flag and picks the eligible parent enrolment).
 *
 * Hides itself when there is no eligible subscription, no expiry date, or the
 * expiry is comfortably in the future.
 */
export function ExtendAccessCta({ hasEligibleSubscription = false, expiresAt }: ExtendAccessCtaProps) {
  const [modalOpen, setModalOpen] = useState(false);

  const expiryInfo = useMemo(() => {
    if (!expiresAt) return null;
    const expiry = new Date(expiresAt);
    if (Number.isNaN(expiry.getTime())) return null;
    const msPerDay = 1000 * 60 * 60 * 24;
    const daysRemaining = Math.ceil((expiry.getTime() - Date.now()) / msPerDay);
    return { expiry, daysRemaining };
  }, [expiresAt]);

  if (!hasEligibleSubscription || !expiryInfo) return null;
  if (expiryInfo.daysRemaining > EXPIRY_WINDOW_DAYS) return null;

  const expired = expiryInfo.daysRemaining < 0;
  const expiryLabel = expiryInfo.expiry.toLocaleDateString();

  return (
    <>
      <section className="rounded-2xl border border-gold/40 bg-gold/[0.06] p-5 shadow-sm dark:border-gold/30 dark:bg-gold/[0.08]">
        <header className="flex items-center gap-2.5">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gold/15 text-gold-fg ring-1 ring-inset ring-gold/30">
            <CalendarClock className="h-4 w-4" aria-hidden="true" />
          </span>
          <h3 className="text-sm font-bold uppercase tracking-wider text-gold-fg">Extend your access</h3>
        </header>
        <p
          className={`mt-2 flex items-center gap-1.5 text-sm font-semibold ${
            expired || expiryInfo.daysRemaining <= 3 ? 'text-danger' : 'text-navy'
          }`}
        >
          <Clock className="h-3.5 w-3.5 flex-none" aria-hidden="true" />
          {expired ? (
            <span>Your course access expired on {expiryLabel}.</span>
          ) : expiryInfo.daysRemaining === 0 ? (
            <span>Your course access expires today ({expiryLabel}).</span>
          ) : (
            <span>
              Your course access expires in {expiryInfo.daysRemaining}{' '}
              {expiryInfo.daysRemaining === 1 ? 'day' : 'days'} ({expiryLabel}).
            </span>
          )}
        </p>
        <p className="mt-1 text-sm text-muted">
          Add 90 more days so you can keep studying without losing your progress.
        </p>
        <button
          type="button"
          onClick={() => setModalOpen(true)}
          className="mt-3 inline-flex items-center justify-center gap-1.5 rounded-lg bg-oet-navy px-4 py-2 text-sm font-bold text-white shadow-sm transition-colors hover:bg-oet-teal focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-gold/50 focus-visible:ring-offset-1"
        >
          <CalendarClock className="h-4 w-4" aria-hidden="true" /> Extend access (+90 days)
        </button>
      </section>

      <AddonPurchaseModal
        open={modalOpen}
        addOnCode={EXTEND_ACCESS_ADDON_CODE}
        addOnLabel="Extend Access — 90 days"
        onClose={() => setModalOpen(false)}
      />
    </>
  );
}
