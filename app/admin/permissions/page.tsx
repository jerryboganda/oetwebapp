'use client';

// Legacy Permission Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Admins & Permissions" tab.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function PermissionsRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=admins');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="Permissions moved to User Operations"
      description="Permission management now lives under Admins & Permissions. Redirecting to the unified workspace."
    />
  );
}
