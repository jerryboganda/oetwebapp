'use client';

/**
 * Retired: warm-up prompts and assessment-criteria PDFs are now managed inside
 * the mock-set wizard's "Exam assets" step (profession-level pool).
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
      title="Shared resources have moved"
      description="Warm-up prompts and assessment-criteria PDFs are now managed in the mock-set wizard. Taking you to the Speaking hub…"
    />
  );
}
