'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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
    <>
      <div className="mx-auto max-w-lg space-y-6">
        <div className="flex items-center justify-between">
          <h1 className="text-xl font-bold text-navy">
            Review Session
          </h1>
          <Link
            href="/reading/vocab"
            className="text-sm font-medium text-primary-600 hover:underline dark:text-primary-400"
          >
            ← Back to Vocab
          </Link>
        </div>

        {loading ? (
          <Skeleton className="h-64 w-full rounded-2xl" />
        ) : items.length === 0 ? (
          <div className="rounded-2xl border border-success/30 bg-success/10 px-8 py-12 text-center">
            <p className="text-4xl" aria-hidden="true">🎉</p>
            <p className="mt-3 text-lg font-semibold text-navy">
              Nothing to review today!
            </p>
            <p className="mt-1 text-sm text-muted">
              Come back tomorrow. Your next session is scheduled by SM-2.
            </p>
            <Button asChild className="mt-5">
              <Link href="/reading/vocab">Back to Vocab Hub</Link>
            </Button>
          </div>
        ) : (
          <VocabReviewSession items={items} onComplete={handleComplete} />
        )}
      </div>
    </>
  );
}
