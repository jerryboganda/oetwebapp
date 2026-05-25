'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { CheckCircle2 } from 'lucide-react';
import { toast } from 'sonner';
import { LearnerDashboardShell } from '@/components/layout';
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

  return (
    <LearnerDashboardShell pageTitle="Vocab Lists">
      <main className="space-y-8">
        <div className="flex items-center justify-between">
          <div>
            <p className="mb-0.5 text-xs font-semibold uppercase tracking-widest text-violet-500">
              Curated Collections
            </p>
            <h1 className="text-2xl font-bold text-neutral-900 dark:text-white">
              Vocabulary Lists
            </h1>
          </div>
          <Link
            href="/reading/vocab"
            className="text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
          >
            ← Back to Vocab
          </Link>
        </div>

        {loading ? (
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            {[0, 1, 2, 3].map((i) => (
              <div
                key={i}
                className="h-48 animate-pulse rounded-2xl border border-neutral-200 bg-neutral-100 dark:border-neutral-700 dark:bg-neutral-800"
              />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            {displayLists.map((list) => (
              <div
                key={list.slug}
                className="flex flex-col rounded-2xl border border-neutral-200 bg-white px-6 py-5 dark:border-neutral-800 dark:bg-neutral-900"
              >
                {/* Header */}
                <div className="flex items-start justify-between gap-4">
                  <div className="flex-1 min-w-0">
                    <h2 className="truncate text-base font-semibold text-neutral-900 dark:text-white">
                      {list.name}
                    </h2>
                    {list.wordCount > 0 ? (
                      <p className="mt-0.5 text-xs text-neutral-400">
                        {list.wordCount.toLocaleString()} words
                      </p>
                    ) : null}
                  </div>

                  {list.isSubscribed ? (
                    <span className="inline-flex shrink-0 items-center gap-1 rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400">
                      <CheckCircle2 className="h-3.5 w-3.5" aria-hidden />
                      Subscribed
                    </span>
                  ) : (
                    <button
                      type="button"
                      disabled={subscribing[list.slug]}
                      onClick={() => void handleSubscribe(list.slug)}
                      className="shrink-0 rounded-lg bg-violet-600 px-4 py-1.5 text-xs font-semibold text-white transition hover:bg-violet-700 disabled:opacity-50"
                    >
                      {subscribing[list.slug] ? 'Subscribing…' : 'Subscribe'}
                    </button>
                  )}
                </div>

                {/* Description */}
                <p className="mt-2 text-sm text-neutral-500 dark:text-neutral-400">
                  {list.description}
                </p>

                {/* Preview words — only when subscribed and words available */}
                {list.isSubscribed && list.previewWords.length > 0 ? (
                  <div className="mt-4 flex flex-wrap gap-2">
                    {list.previewWords.slice(0, 5).map((word) => (
                      <span
                        key={word}
                        className="rounded-full border border-violet-200 bg-violet-50 px-2.5 py-0.5 text-xs font-medium text-violet-700 dark:border-violet-800/50 dark:bg-violet-950/30 dark:text-violet-300"
                      >
                        {word}
                      </span>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        )}
      </main>
    </LearnerDashboardShell>
  );
}