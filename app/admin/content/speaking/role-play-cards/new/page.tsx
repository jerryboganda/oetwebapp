'use client';

/** Retired: new role-play cards are now authored via the card wizard. */

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function Page() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/speaking/cards/new');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="New role-play card moved"
      description="Authoring is now a guided wizard. Taking you there…"
    />
  );
}
