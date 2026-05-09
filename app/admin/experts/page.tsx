'use client';

// Legacy Expert Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Tutors" tab. This stub redirects any
// existing bookmarks or deep links to the new location.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function ExpertsRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=tutors');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="Expert Management moved to User Operations"
      description="Tutor and expert administration now lives under the Tutors tab. Redirecting to the unified workspace."
    />
  );
}
