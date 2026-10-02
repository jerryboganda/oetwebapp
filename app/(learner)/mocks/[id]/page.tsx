'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Layers } from 'lucide-react';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { fetchMockReport, fetchMockSession, isApiError } from '@/lib/api';

export default function MockRouteRedirectPage() {
  const params = useParams();
  const router = useRouter();
  const id = Array.isArray(params?.id) ? params.id[0] : params?.id;
  const [error, setError] = useState<string | null>(null);
  const missingIdError = id ? null : 'This mock link is missing its id.';

  useEffect(() => {
    const mockId = id ?? '';
    if (!mockId) {
      return;
    }

    let cancelled = false;

    async function resolveMockRoute() {
      try {
        await fetchMockReport(mockId);
        if (!cancelled) {
          router.replace(`/mocks/report/${mockId}`);
        }
        return;
      } catch (reportError) {
        if (cancelled) {
          return;
        }

        try {
          await fetchMockSession(mockId);
          if (!cancelled) {
            router.replace(`/mocks/player/${mockId}`);
          }
        } catch (sessionError) {
          if (!cancelled) {
            const fallbackError = isApiError(sessionError)
              ? sessionError.userMessage
              : isApiError(reportError)
                ? reportError.userMessage
                : 'We could not resolve this mock link.';
            setError(fallbackError);
          }
        }
      }
    }

    void resolveMockRoute();

    return () => {
      cancelled = true;
    };
  }, [id, router]);

  const visibleError = missingIdError ?? error;

  return visibleError ? (
    <ErrorState message={visibleError} onRetry={() => router.push('/mocks')} retryLabel="Back To Mocks" />
  ) : (
    <EmptyState
      icon={<Layers className="h-7 w-7" aria-hidden="true" />}
      title="Opening your mock..."
      description="We are routing you to the correct mock player or report."
    />
  );
}
