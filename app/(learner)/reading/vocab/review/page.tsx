'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { EmptyState } from '@/components/ui/empty-error';
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

  // The breadcrumb's "Vocab" crumb is the way back, so no back link in the header.
  return (
    <>
      <LearnerPageHero
        eyebrow="SM-2 Spaced Repetition"
        icon={RefreshCw}
        title="Review Session"
        description=""
      />

      {/* A flashcard reads best in a narrow column: the session caps its own
          width, the page frame stays full width. */}
      <div className="mx-auto w-full max-w-lg">
        {loading ? (
          <Skeleton className="h-64 w-full rounded-2xl" />
        ) : items.length === 0 ? (
          <EmptyState
            className="border-solid border-success/30 bg-success/10"
            icon={<span className="text-4xl">🎉</span>}
            title="Nothing to review today!"
            description="Come back tomorrow. Your next session is scheduled by SM-2."
            action={{ label: 'Back to Vocab Hub', href: '/reading/vocab' }}
          />
        ) : (
          <VocabReviewSession items={items} onComplete={handleComplete} />
        )}
      </div>
    </>
  );
}
