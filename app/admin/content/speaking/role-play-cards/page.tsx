'use client';

/**
 * Retired: role-play card authoring now lives in the unified Speaking hub +
 * card wizard (/admin/speaking). This stub redirects so old links keep working.
 */

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
      title="Speaking authoring has moved"
      description="Role-play cards and mock sets now live in one place. Taking you to the Speaking hub…"
    />
  );
}
