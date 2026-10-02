'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Plus, Sparkles, CheckCircle2 } from 'lucide-react';
import { fetchVocabularyTerms, addToMyVocabulary } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyTerm } from '@/lib/types/vocabulary';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';

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
      <Card padding="lg" className="border-primary/20 bg-primary/5" role="status" aria-busy="true" aria-label="Loading words to review">
        <div className="eyebrow text-primary">Words to Review</div>
        <Skeleton className="mt-2 h-16" />
      </Card>
    );
  }

  if (terms.length === 0) return null;

  return (
    <MotionSection delayIndex={3}>
      <Card padding="lg" className="border-primary/20 bg-primary/5">
        <div className="mb-4 flex items-start gap-3">
          <BookOpen className="h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
          <div className="min-w-0 flex-1">
            <div className="mb-1 flex items-center gap-2 eyebrow text-primary">
              <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
              Words to Review
            </div>
            <h3 className="text-lg font-bold text-navy">
              Strengthen your {weakSubtest} vocabulary
            </h3>
            <p className="mt-1 text-sm text-muted">
              Based on your weakest criterion ({weakCriterion}), here are OET terms to add to your word bank.
            </p>
          </div>
          <Button asChild variant="outline" size="sm" className="shrink-0">
            <Link href="/vocabulary">View bank</Link>
          </Button>
        </div>

        <ul className="space-y-2">
          {terms.map(term => {
            const isAdded = added.has(term.id);
            return (
              <li
                key={term.id}
                className="flex items-start gap-3 rounded-xl border border-border bg-surface p-3"
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
                <Button
                  size="sm"
                  variant={isAdded ? 'ghost' : 'primary'}
                  onClick={() => handleAdd(term)}
                  disabled={isAdded}
                  aria-label={isAdded ? `${term.term} added` : `Add ${term.term} to my list`}
                  className={isAdded ? 'shrink-0 text-success-strong disabled:opacity-100' : 'shrink-0'}
                >
                  {isAdded
                    ? (<><CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" /> Added</>)
                    : (<><Plus className="h-3.5 w-3.5" aria-hidden="true" /> Save</>)}
                </Button>
              </li>
            );
          })}
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
