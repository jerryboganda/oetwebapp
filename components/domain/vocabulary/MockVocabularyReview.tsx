'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Plus, Sparkles, CheckCircle2 } from 'lucide-react';
import { fetchVocabularyTerms, addToMyVocabulary } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyTerm } from '@/lib/types/vocabulary';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';

export interface MockVocabularyReviewProps {
  mockId: string;
  /** Weakest-criterion subtest ("writing" | "speaking" | "reading" | "listening"). */
  weakSubtest: string;
  /** Weakest-criterion name for contextual messaging. */
  weakCriterion: string;
  /** Short description of the weak area. */
  weakDescription: string;
  /** How many suggestions to show. Defaults to 8. */
  count?: number;
}

/**
 * MockVocabularyReview — post-mock "Words to Review" card.
 *
 * Surfaces up to `count` vocabulary terms relevant to the learner's weakest
 * subtest/criterion, with one-click "Add to my list" buttons. Writes the
 * mock's id into the saved term's sourceRef so the learner can trace back
 * which exam surfaced the word.
 */
export function MockVocabularyReview({
  mockId,
  weakSubtest,
  weakCriterion,
  weakDescription,
  count = 8,
}: MockVocabularyReviewProps) {
  const [terms, setTerms] = useState<VocabularyTerm[]>([]);
  const [loading, setLoading] = useState(true);
  const [added, setAdded] = useState<Set<string>>(new Set());

  useEffect(() => {
    const category = mapSubtestToCategory(weakSubtest);
    void (async () => {
      try {
        const res = await fetchVocabularyTerms({
          examTypeCode: 'oet',
          category,
          pageSize: count,
          page: 1,
        });
        const items = Array.isArray(res) ? res : ((res as { items?: VocabularyTerm[] }).items ?? []);
        setTerms((items as VocabularyTerm[]).slice(0, count));
      } finally {
        setLoading(false);
      }
    })();
  }, [weakSubtest, count]);

  async function handleAdd(term: VocabularyTerm) {
    if (added.has(term.id)) return;
    try {
      await addToMyVocabulary(term.id, {
        sourceRef: `mock:${mockId}:${weakSubtest}`,
        context: weakDescription,
      });
      analytics.track('vocab_saved_from_mock', { mockId, termId: term.id, subtest: weakSubtest });
      setAdded(prev => new Set(prev).add(term.id));
    } catch {/* silent — button will remain available */}
  }

  if (loading) {
    return (
      <Card className="rounded-3xl border-primary/20 bg-primary/5 p-6 shadow-sm">
        <div className="text-xs font-black uppercase tracking-widest text-primary">Words to Review</div>
        <div className="mt-2 h-16 animate-pulse rounded bg-primary/10" />
      </Card>
    );
  }

  if (terms.length === 0) return null;

  return (
    <MotionSection delayIndex={3}>
      <Card className="rounded-3xl border-primary/20 bg-gradient-to-br from-primary/5 to-indigo-50 p-6 shadow-sm">
        <div className="mb-4 flex items-start gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-white/70 text-primary">
            <BookOpen className="h-5 w-5" />
          </div>
          <div className="flex-1">
            <div className="mb-1 flex items-center gap-2 text-xs font-black uppercase tracking-widest text-primary">
              <Sparkles className="h-3.5 w-3.5" />
              Words to Review
            </div>
            <h3 className="text-lg font-black text-navy">
              Strengthen your {weakSubtest} vocabulary
            </h3>
            <p className="mt-1 text-sm text-muted">
              Based on your weakest criterion ({weakCriterion}), here are OET terms to add to your word bank.
            </p>
          </div>
          <Link
            href="/vocabulary"
            className="inline-flex shrink-0 items-center gap-1 rounded-full bg-white px-3 py-1.5 text-xs font-semibold text-primary shadow-sm hover:bg-primary/10"
          >
            View bank
          </Link>
        </div>

        <ul className="space-y-2">
          {terms.map(term => (
            <li
              key={term.id}
              className="flex items-start gap-3 rounded-2xl border border-white/70 bg-white/70 p-3"
            >
              <div className="flex-1 min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <Link
                    href={`/vocabulary/terms/${encodeURIComponent(term.id)}`}
                    className="text-sm font-bold text-navy hover:underline"
                  >
                    {term.term}
                  </Link>
                  {term.ipaPronunciation && (
                    <span className="text-xs italic text-muted">{term.ipaPronunciation}</span>
                  )}
                </div>
                <p className="line-clamp-2 text-xs text-muted">{term.definition}</p>
              </div>
              <button
                onClick={() => handleAdd(term)}
                disabled={added.has(term.id)}
                aria-label={added.has(term.id) ? `${term.term} added` : `Add ${term.term} to my list`}
                className={`inline-flex shrink-0 items-center gap-1 rounded-full px-3 py-1.5 text-xs font-medium ${
                  added.has(term.id)
                    ? 'bg-green-50 text-green-600'
                    : 'bg-primary text-white hover:bg-primary/90'
                } transition-colors`}
              >
                {added.has(term.id)
                  ? (<><CheckCircle2 className="h-3 w-3" /> Added</>)
                  : (<><Plus className="h-3 w-3" /> Save</>)}
              </button>
            </li>
          ))}
        </ul>
      </Card>
    </MotionSection>
  );
}

function mapSubtestToCategory(subtest: string): string | undefined {
  // Conservative mapping — if no good match, we return undefined and the
  // backend returns a generic selection across active terms.
  const normalised = subtest.toLowerCase();
  if (normalised.includes('writ')) return 'clinical_communication';
  if (normalised.includes('speak')) return 'clinical_communication';
  if (normalised.includes('read')) return 'conditions';
  if (normalised.includes('listen')) return 'symptoms';
  return undefined;
}
