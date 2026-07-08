'use client';

/**
 * Mock entry-point consolidation — Speaking mocks are now booked and started
 * exclusively from the unified Mock Center (`/mocks`), the only place that
 * runs the shared credit/entitlement check (MockEntitlementService) before a
 * mock attempt is created. This standalone catalog page (and its dead
 * `[id]` "bridge" redirect, which discarded the mock-set selection) is
 * retired in favor of that single entry point.
 */
import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';

export default function RetiredSpeakingMocksIndexPage() {
  const router = useRouter();

  useEffect(() => {
    const t = window.setTimeout(() => router.replace('/mocks?subtest=speaking'), 1200);
    return () => window.clearTimeout(t);
  }, [router]);

  return (
    <div className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center px-4 text-center">
      <Loader2 className="h-6 w-6 animate-spin text-muted" />
      <h1 className="mt-4 text-lg font-semibold text-foreground">Speaking mocks have moved</h1>
      <p className="mt-2 text-sm text-muted">
        Book and start every mock exam from the Mock Center now. Taking you there…
      </p>
      <Link href="/mocks?subtest=speaking" className="mt-4 text-sm font-medium text-primary hover:underline">
        Go to Mock Center now
      </Link>
    </div>
  );
}
