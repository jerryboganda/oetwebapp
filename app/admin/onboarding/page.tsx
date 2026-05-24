'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Compass } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';

export default function AdminOnboardingIndex() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/onboarding/interlocutor');
  }, [router]);

  return (
    <AdminTableLayout
      title="Onboarding"
      description="Redirecting to the interlocutor onboarding workflow."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Onboarding' },
      ]}
    >
      <EmptyState
        illustration={<Compass />}
        title="Redirecting to interlocutor onboarding"
        description="The onboarding hub lives under the interlocutor workflow. Taking you there now."
        primaryAction={{
          label: 'Go to interlocutor onboarding',
          href: '/admin/onboarding/interlocutor',
        }}
      />
    </AdminTableLayout>
  );
}
