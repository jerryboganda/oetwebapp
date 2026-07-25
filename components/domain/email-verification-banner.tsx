'use client';

import Link from 'next/link';
import { useAuth } from '@/contexts/auth-context';
import { InlineAlert } from '@/components/ui/alert';

/**
 * Course Platform Security Requirements §4.2 — owner decision: banner first,
 * hard gate later. Shows on every authenticated page (mounted inside
 * AppShell) for an account whose email is not yet verified; never blocks
 * access. `SecurityRequireVerifiedEmailForLearners`-style gating, if the
 * owner asks for it later, is a separate follow-up — this component makes no
 * access decisions.
 */
export function EmailVerificationBanner() {
  const { user } = useAuth();

  if (!user || user.isEmailVerified) {
    return null;
  }

  return (
    <div className="px-4 pt-4 lg:px-6 lg:pt-6">
      <InlineAlert
        variant="warning"
        dismissible
        action={
          <Link
            href={`/verify-email?email=${encodeURIComponent(user.email)}`}
            className="whitespace-nowrap text-sm font-semibold underline underline-offset-2"
          >
            Verify now
          </Link>
        }
      >
        Please verify your email address to keep full access to your account.
      </InlineAlert>
    </div>
  );
}
