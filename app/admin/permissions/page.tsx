'use client';

// Legacy Permission Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Admins & Permissions" tab.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function PermissionsRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=admins');
  }, [router]);
  return (
    <div className="p-8 text-sm text-muted">
      Permissions has moved into User Operations → Admins &amp; Permissions. Redirecting…
    </div>
  );
}
