'use client';

// Legacy Permission Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Admins & Permissions" tab. We use
// client-side replace so the page can wear the admin design system chrome.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Shield } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';

export default function PermissionsRedirectPage() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/users?tab=admins');
  }, [router]);

  return (
    <AdminTableLayout
      title="Permissions"
      description="Permission management has moved to the unified User Operations hub."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Permissions' },
      ]}
    >
      <EmptyState
        illustration={<Shield />}
        title="Redirecting to Admins & Permissions"
        description="Permission management is now part of the User Operations hub. Taking you to the Admins & Permissions tab."
        primaryAction={{
          label: 'Open Admins & Permissions',
          href: '/admin/users?tab=admins',
        }}
      />
    </AdminTableLayout>
  );
}
