'use client';

/**
 * Retired: hidden card types are now created inline in the card wizard's
 * Classification step.
 */

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { AdminRouteRedirectNotice } from '@/components/domain/admin-route-surface';

export default function Page() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/speaking/cards/new');
  }, [router]);
  return (
    <AdminRouteRedirectNotice
      title="Card types have moved"
      description="Create hidden card types inline while classifying a card. Taking you to the card wizard…"
    />
  );
}
