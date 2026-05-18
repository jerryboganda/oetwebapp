'use client';

import type { ReactNode } from 'react';

import { useSponsorAuth } from '@/lib/hooks/use-sponsor-auth';

export function SponsorAuthGate({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading, role } = useSponsorAuth();

  if (isLoading || !isAuthenticated || role !== 'sponsor') {
    return null;
  }

  return <>{children}</>;
}
