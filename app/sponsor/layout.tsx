import { notFound } from 'next/navigation';
import type { ReactNode } from 'react';

import { SponsorAuthGate } from './sponsor-auth-gate';

export default function SponsorLayout({ children }: { children: ReactNode }) {
  if (process.env.NEXT_PUBLIC_SPONSOR_PORTAL_ENABLED !== 'true') {
    notFound();
  }

  return <SponsorAuthGate>{children}</SponsorAuthGate>;
}
