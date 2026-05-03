'use client';

import Link from 'next/link';
import { motion, useReducedMotion } from 'motion/react';
import { ArrowLeft } from 'lucide-react';

/**
 * Breadcrumb-style "Back to billing" link used at the top of the
 * billing sub-pages (upgrade, score-guarantee, referral) to keep
 * navigation between the new tabbed billing center and its sub-pages
 * fluent and consistent.
 */
export function BackToBillingLink({ label = 'Back to billing' }: { label?: string }) {
  const prefersReducedMotion = useReducedMotion();
  const initial = prefersReducedMotion ? false : { opacity: 0, x: -4 };
  const animate = prefersReducedMotion ? undefined : { opacity: 1, x: 0 };

  return (
    <motion.div
      initial={initial}
      animate={animate}
      transition={{ duration: 0.2, ease: 'easeOut' }}
    >
      <Link
        href="/billing"
        aria-label="Back to billing center"
        className="inline-flex items-center gap-1.5 rounded-md text-xs font-semibold uppercase tracking-[0.16em] text-muted transition-colors hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        {label}
      </Link>
    </motion.div>
  );
}
