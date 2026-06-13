'use client';

/** Retired: mock sets are now composed via the Speaking hub + mock-set wizard. */

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function Page() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/speaking');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="Mock sets have moved"
      description="Compose mock sets from the Speaking hub. Taking you there…"
    />
  );
}
