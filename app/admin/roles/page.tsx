'use client';

// Legacy Roles surface. Consolidated into the unified User Operations hub at
// /admin/users → "Admins & Permissions" tab (same backend, presets included).

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function RolesRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=admins');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="Roles moved to User Operations"
      description="Roles now live under Admins & Permissions. Redirecting to the unified workspace."
    />
  );
}
