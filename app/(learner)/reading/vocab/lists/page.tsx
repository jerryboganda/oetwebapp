'use client';

import { useEffect, useState } from 'react';
import { CheckCircle2, Library } from 'lucide-react';
import { toast } from 'sonner';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import {
  getVocabLists,
  subscribeToVocabList,
  type VocabularyListDto,
} from '@/lib/reading-pathway-api';

const CURATED_SLUGS = [
  'top-200-oet-medical-terms',
  'medicine-and-surgery',
  'nursing-and-allied-health',
  'pharmacy-and-pharmacology',
];

const CURATED_META: Record<string, { name: string; description: string }> = {
  'top-200-oet-medical-terms': {
    name: 'Top 200 OET Medical Terms',
    description: 'The highest-frequency medical vocabulary that appears across all OET reading subtests.',
  },
  'medicine-and-surgery': {
    name: 'Medicine & Surgery',
    description: 'Core clinical terminology covering diagnosis, procedures, and treatment in medicine and surgery.',
  },
  'nursing-and-allied-health': {
    name: 'Nursing & Allied Health',
    description: 'Vocabulary essential for nursing, physiotherapy, occupational therapy, and related professions.',
  },
  'pharmacy-and-pharmacology': {
    name: 'Pharmacy & Pharmacology',
    description: 'Drug classes, mechanisms of action, routes of administration, and clinical pharmacology terms.',
  },
};

export default function VocabListsPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [lists, setLists] = useState<VocabularyListDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [subscribing, setSubscribing] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) { setLoading(false); return; }

    let cancelled = false;
    (async () => {
      try {
        const fetched = await getVocabLists();
        if (!cancelled) setLists(fetched);
      } catch {
        // show curated stubs even if API fails
        if (!cancelled) setLists(
          CURATED_SLUGS.map((slug) => ({
            id: slug,
            slug,
            name: CURATED_META[slug].name,
            description: CURATED_META[slug].description,
            wordCount: 0,
            isSubscribed: false,
            previewWords: [],
          })),
        );
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authLoading, isAuthenticated]);

  async function handleSubscribe(slug: string) {
    if (subscribing[slug]) return;
    setSubscribing((s) => ({ ...s, [slug]: true }));
    try {
      await subscribeToVocabList(slug);
      setLists((prev) =>
        prev.map((l) => l.slug === slug ? { ...l, isSubscribed: true } : l),
      );
      toast.success('Subscribed! Words added to your deck.');
    } catch {
      toast.error('Could not subscribe. Please try again.');
    } finally {
      setSubscribing((s) => ({ ...s, [slug]: false }));
    }
  }

  // Merge API data with curated stubs for display order
  const displayLists: VocabularyListDto[] = CURATED_SLUGS.map((slug) => {
    const fromApi = lists.find((l) => l.slug === slug);
    const meta = CURATED_META[slug];
    return fromApi ?? {
      id: slug,
      slug,
      name: meta.name,
      description: meta.description,
      wordCount: 0,
      isSubscribed: false,
      previewWords: [],
    };
  });

  // The breadcrumb's "Vocab" crumb is the way back, so no back link in the header.
  return (
    <>
      <LearnerPageHero
        eyebrow="Curated Collections"
        icon={Library}
        title="Vocabulary Lists"
        description=""
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-48 rounded-2xl" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          {displayLists.map((list, index) => (
            <MotionItem key={list.slug} delayIndex={Math.min(index, 5)} className="h-full">
              <Card className="flex h-full flex-col">
                {/* Header */}
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0 flex-1">
                    <h2 className="text-base font-semibold text-navy">
                      {list.name}
                    </h2>
                    {list.wordCount > 0 ? (
                      <p className="mt-0.5 text-xs tabular-nums text-muted">
                        {list.wordCount.toLocaleString()} words
                      </p>
                    ) : null}
                  </div>

                  {list.isSubscribed ? (
                    <Badge variant="success" size="md" className="shrink-0 gap-1">
                      <CheckCircle2 className="h-3.5 w-3.5" aria-hidden />
                      Subscribed
                    </Badge>
                  ) : (
                    <Button
                      size="sm"
                      className="shrink-0"
                      disabled={subscribing[list.slug]}
                      onClick={() => void handleSubscribe(list.slug)}
                    >
                      {subscribing[list.slug] ? 'Subscribing…' : 'Subscribe'}
                    </Button>
                  )}
                </div>

                {/* Description */}
                <p className="mt-2 text-sm text-muted">
                  {list.description}
                </p>

                {/* Preview words: only when subscribed and words available */}
                {list.isSubscribed && list.previewWords.length > 0 ? (
                  <div className="mt-4 flex flex-wrap gap-2">
                    {list.previewWords.slice(0, 5).map((word) => (
                      <Badge key={word} variant="violet" className="font-medium">
                        {word}
                      </Badge>
                    ))}
                  </div>
                ) : null}
              </Card>
            </MotionItem>
          ))}
        </div>
      )}
    </>
  );
}
