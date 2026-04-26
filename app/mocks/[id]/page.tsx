'use client';

import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { fetchMockReport, fetchMockSession, isApiError } from '@/lib/api';
import { useParams, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';

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

  return (
    <LearnerDashboardShell pageTitle="Mocks" backHref="/mocks">
      {!visibleError ? (
        <div className="flex min-h-[40vh] items-center justify-center px-4">
          <div className="text-center">
            <p className="text-lg font-semibold text-navy">Opening your mock...</p>
            <p className="mt-2 text-sm text-muted">We are routing you to the correct mock player or report.</p>
          </div>
        </div>
      ) : (
        <div className="mx-auto max-w-xl space-y-4 rounded-3xl border border-border bg-surface p-6 shadow-sm">
          <InlineAlert variant="error">{visibleError}</InlineAlert>
          <div className="flex justify-end">
            <Button type="button" onClick={() => router.push('/mocks')}>
              Back To Mocks
            </Button>
          </div>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
