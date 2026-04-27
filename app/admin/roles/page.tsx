'use client';

// Legacy Roles surface. Consolidated into the unified User Operations hub at
// /admin/users → "Admins & Permissions" tab (same backend, presets included).

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function RolesRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=admins');
  }, [router]);
  return (
    <div className="p-8 text-sm text-muted">
      Roles has moved into User Operations → Admins &amp; Permissions. Redirecting…
    </div>
  );
}
