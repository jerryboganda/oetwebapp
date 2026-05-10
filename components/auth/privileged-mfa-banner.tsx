'use client';

import { usePathname, useRouter } from 'next/navigation';
import { ShieldCheck } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';

function buildSetupHref(pathname: string | null) {
  const nextPath = pathname?.trim();
  if (!nextPath || !nextPath.startsWith('/')) {
    return '/mfa/setup';
  }

  return `/mfa/setup?next=${encodeURIComponent(nextPath)}`;
}

export function PrivilegedMfaBanner() {
  const router = useRouter();
  const pathname = usePathname();
  const { user } = useAuth();

  if (!user) {
    return null;
  }

  const isPrivilegedUser = user.role === 'expert' || user.role === 'admin';
  if (!isPrivilegedUser || user.isAuthenticatorEnabled) {
    return null;
  }

  const setupHref = buildSetupHref(pathname);

  return (
    <div>
      <div className="absolute top-0 left-0 right-0 z-40 flex h-[30px] items-center justify-center bg-gradient-to-r from-orange-500 to-rose-600 px-4 text-xs font-medium text-white shadow-sm">
        <ShieldCheck className="mr-2 h-3.5 w-3.5 opacity-90" />
        <span className="truncate">
          Security notice: Multi-factor authentication is recommended for privileged access.
        </span>
        <button
          onClick={() => router.push(setupHref)}
          className="ml-3 shrink-0 rounded bg-white/20 px-2 py-0.5 transition-colors hover:bg-white/30 font-semibold"
        >
          Set up MFA
        </button>
      </div>
      <div className="h-4 lg:h-2" aria-hidden="true" />
    </div>
  );
}
