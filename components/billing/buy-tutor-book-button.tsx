'use client';

import { useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { AddonPurchaseModal } from './addon-purchase-modal';

/**
 * Single entry point for buying The Tutor Book from anywhere in the
 * storefront. Eligibility — and therefore £32 vs £45 — is resolved
 * server-side inside AddonPurchaseModal; the candidate never picks a price.
 */
export function BuyTutorBookButton({ className, children }: { className?: string; children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, loading } = useAuth();
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className={className}
        onClick={() => {
          if (loading) return;
          if (!isAuthenticated) {
            router.push(`/sign-in?next=${encodeURIComponent(pathname || '/')}`);
            return;
          }
          setOpen(true);
        }}
      >
        {children}
      </button>
      <AddonPurchaseModal
        open={open}
        addOnCode="tutor-book-addon"
        addOnLabel="The Tutor Book"
        addOnPriceGbp={32}
        onClose={() => setOpen(false)}
      />
    </>
  );
}
