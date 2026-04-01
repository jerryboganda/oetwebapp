'use client';

import { usePathname, useRouter } from 'next/navigation';
import { ShieldCheck } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
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
    <InlineAlert
      variant="warning"
      title="Recommended security step"
      action={(
        <Button variant="outline" size="sm" onClick={() => router.push(setupHref)}>
          <ShieldCheck className="h-4 w-4" />
          Set up MFA
        </Button>
      )}
    >
      Multi-factor authentication is recommended for privileged access. You can keep working without it, but enabling an authenticator app adds a much stronger layer of account protection.
    </InlineAlert>
  );
}
