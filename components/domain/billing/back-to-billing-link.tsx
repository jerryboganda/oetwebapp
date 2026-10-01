'use client';

import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';

/**
 * "Back to billing" link for the billing sub-pages (score-guarantee,
 * referral). It sits in the page hero's aside, so the way back to the
 * billing center stays one tap away without a second header row.
 */
export function BackToBillingLink({ label = 'Back to billing' }: { label?: string }) {
  return (
    <Button asChild variant="outline" size="sm">
      <Link href="/billing" aria-label="Back to billing center">
        <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
        {label}
      </Link>
    </Button>
  );
}
