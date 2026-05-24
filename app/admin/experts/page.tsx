'use client';

// Legacy Expert Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Tutors" tab. We use client-side replace
// so the page can still wear the admin design system chrome briefly.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { GraduationCap } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';

export default function ExpertsRedirectPage() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/users?tab=tutors');
  }, [router]);

  return (
    <AdminTableLayout
      title="Experts"
      description="Expert management has moved to the unified User Operations hub."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Experts' },
      ]}
    >
      <EmptyState
        illustration={<GraduationCap />}
        title="Redirecting to Tutors"
        description="Expert management is now part of the User Operations hub. Taking you to the Tutors tab."
        primaryAction={{
          label: 'Open Tutors tab',
          href: '/admin/users?tab=tutors',
        }}
      />
    </AdminTableLayout>
  );
}
