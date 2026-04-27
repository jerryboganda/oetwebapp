'use client';

// Legacy Expert Management surface. Consolidated into the unified User
// Operations hub at /admin/users → "Tutors" tab. This stub redirects any
// existing bookmarks or deep links to the new location.

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

export default function ExpertsRedirectPage() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/admin/users?tab=tutors');
  }, [router]);
  return (
    <div className="p-8 text-sm text-muted">
      Expert Management has moved into User Operations → Tutors. Redirecting…
    </div>
  );
}
