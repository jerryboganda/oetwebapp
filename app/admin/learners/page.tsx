'use client';

// Learners index route. Consolidated into the unified User Operations hub at
// /admin/users → "Learners" tab. Drill-in detail pages still live under
// /admin/learners/[userId]/...

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Users } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';

export default function LearnersRedirectPage() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/users?tab=learners');
  }, [router]);

  return (
    <AdminTableLayout
      title="Learners"
      description="Learner management has moved to the unified User Operations hub."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Learners' },
      ]}
    >
      <EmptyState
        illustration={<Users />}
        title="Redirecting to Learners"
        description="Learner management is now part of the User Operations hub. Taking you to the Learners tab."
        primaryAction={{
          label: 'Open Learners tab',
          href: '/admin/users?tab=learners',
        }}
      />
    </AdminTableLayout>
  );
}
