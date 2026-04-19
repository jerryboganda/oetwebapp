'use client';

import { useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { ShieldCheck, X } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { cn } from '@/lib/utils';

/**
 * Privileged MFA reminder chip.
 *
 * Design intent (per DESIGN.md §5 "hero-card rhythm" and §8 "motion
 * clarifies hierarchy"):
 * - Admins and experts should be gently nudged toward enabling MFA, not
 *   shouted at. The full-bleed amber `InlineAlert` we used previously
 *   dominated the first screenful of every admin page, pushing the actual
 *   hero below the fold.
 * - This variant is a single-line pill that sits beneath the top nav and
 *   right of the main content width. It stays visible but never competes
 *   with the page hero.
 * - The learner can dismiss it per-session. We don't persist the dismiss
 *   flag server-side; a page reload re-shows the chip so a genuinely
 *   absent MFA setup is never silently hidden long-term.
 */
export function PrivilegedMfaBanner() {
  const router = useRouter();
  const pathname = usePathname();
  const { user } = useAuth();

  // Lazy initialiser reads sessionStorage once on mount WITHOUT useEffect
  // so the first render is already correct and we don't trigger the
  // "setState inside effect" lint rule. SSR returns false (sessionStorage
  // unavailable) which is the safe default — the chip shows until
  // hydration picks up the stored dismissal.
  const [dismissed, setDismissed] = useState<boolean>(() => {
    try {
      if (typeof window === 'undefined') return false;
      return window.sessionStorage.getItem('oet.mfa-chip.dismissed') === '1';
    } catch {
      return false;
    }
  });

  if (!user) return null;

  const isPrivileged = user.role === 'expert' || user.role === 'admin';
  if (!isPrivileged || user.isAuthenticatorEnabled || dismissed) return null;

  const setupHref = buildSetupHref(pathname);

  const handleDismiss = () => {
    setDismissed(true);
    try {
      window.sessionStorage.setItem('oet.mfa-chip.dismissed', '1');
    } catch {
      /* ignore */
    }
  };

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        'mb-4 flex items-center justify-between gap-3 rounded-full border border-amber-200 bg-amber-50/80 pl-3 pr-1.5 py-1.5 text-amber-900 shadow-sm',
        'sm:gap-4 sm:py-2'
      )}
    >
      <div className="flex items-center gap-2 min-w-0">
        <ShieldCheck className="h-4 w-4 flex-shrink-0 text-amber-700" aria-hidden />
        <p className="truncate text-sm">
          <span className="font-semibold">Enable MFA</span>
          <span className="hidden sm:inline text-amber-800/80"> — recommended for privileged access.</span>
        </p>
      </div>
      <div className="flex items-center gap-1 flex-shrink-0">
        <button
          type="button"
          onClick={() => router.push(setupHref)}
          className="inline-flex items-center gap-1 rounded-full bg-amber-700 px-3 py-1 text-xs font-semibold text-white hover:bg-amber-800 transition-colors"
        >
          Set up
        </button>
        <button
          type="button"
          onClick={handleDismiss}
          aria-label="Dismiss MFA reminder"
          className="inline-flex items-center justify-center rounded-full p-1 text-amber-700/80 hover:text-amber-900 hover:bg-amber-100 transition-colors"
        >
          <X className="h-3.5 w-3.5" aria-hidden />
        </button>
      </div>
    </div>
  );
}

function buildSetupHref(pathname: string | null): string {
  const nextPath = pathname?.trim();
  if (!nextPath || !nextPath.startsWith('/')) return '/mfa/setup';
  return `/mfa/setup?next=${encodeURIComponent(nextPath)}`;
}
