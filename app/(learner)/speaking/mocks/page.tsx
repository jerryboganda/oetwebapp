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
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';

export default function RetiredSpeakingMocksIndexPage() {
  const router = useRouter();

  useEffect(() => {
    const t = window.setTimeout(() => router.replace('/mocks?subtest=speaking'), 1200);
    return () => window.clearTimeout(t);
  }, [router]);

  return (
    <Card padding="lg" className="flex flex-col items-center py-12 text-center" role="status">
      <Loader2 className="h-6 w-6 animate-spin text-muted" aria-hidden="true" />
      <h1 className="mt-4 text-lg font-bold text-navy">Speaking mocks have moved</h1>
      <p className="mt-2 max-w-md text-sm text-muted">
        Book and start every mock exam from the Mock Center now. Taking you there…
      </p>
      <Button asChild variant="outline" size="sm" className="mt-4">
        <Link href="/mocks?subtest=speaking">Go to Mock Center now</Link>
      </Button>
    </Card>
  );
}
