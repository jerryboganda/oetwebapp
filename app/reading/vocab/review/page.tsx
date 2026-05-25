'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
import { useAuth } from '@/contexts/auth-context';
import { getVocabDue, type VocabItemDto } from '@/lib/reading-pathway-api';
import VocabReviewSession from '@/components/reading/VocabReviewSession';

export default function VocabReviewPage() {
  const router = useRouter();
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [items, setItems] = useState<VocabItemDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) { setLoading(false); return; }

    let cancelled = false;
    (async () => {
      try {
        const due = await getVocabDue();
        if (!cancelled) setItems(due);
      } catch {
        // leave items empty — UI handles it
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authLoading, isAuthenticated]);

  function handleComplete() {
    toast.success('Session complete! Great work.');
    router.push('/reading/vocab');
  }

  return (
    <LearnerDashboardShell pageTitle="Vocab Review">
      <main className="mx-auto max-w-lg space-y-6">
        <div className="flex items-center justify-between">
          <h1 className="text-xl font-bold text-neutral-900 dark:text-white">
            Review Session
          </h1>
          <Link
            href="/reading/vocab"
            className="text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
          >
            ← Back to Vocab
          </Link>
        </div>

        {loading ? (
          <div className="flex h-64 items-center justify-center">
            <div className="h-10 w-10 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
          </div>
        ) : items.length === 0 ? (
          <div className="rounded-2xl border border-emerald-200 bg-emerald-50 px-8 py-12 text-center dark:border-emerald-900/50 dark:bg-emerald-950/30">
            <p className="text-4xl">🎉</p>
            <p className="mt-3 text-lg font-semibold text-neutral-900 dark:text-white">
              Nothing to review today!
            </p>
            <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
              Come back tomorrow — your next session is scheduled by SM-2.
            </p>
            <Link
              href="/reading/vocab"
              className="mt-5 inline-flex rounded-xl bg-violet-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-violet-700"
            >
              Back to Vocab Hub
            </Link>
          </div>
        ) : (
          <VocabReviewSession items={items} onComplete={handleComplete} />
        )}
      </main>
    </LearnerDashboardShell>
  );
}