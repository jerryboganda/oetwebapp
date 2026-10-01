'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Volume2, Plus, CheckCircle2, Trash2, Lock } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Badge, CategoryBadge, RecallTierBadge, type BadgeProps } from '@/components/ui/badge';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  fetchVocabularyTerm,
  fetchMyVocabulary,
  addToMyVocabulary,
  removeFromMyVocabulary,
  fetchRecallsAudio,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import { cleanExampleSentence } from '@/lib/vocabulary-example-sentence';
import { PracticeSpelling } from '@/components/domain/recalls/practice-spelling';
import type { VocabularyTerm, LearnerVocabulary } from '@/lib/types/vocabulary';

const MASTERY_BADGES: Record<string, BadgeProps['variant']> = {
  new: 'slate',
  learning: 'info',
  reviewing: 'warning',
  mastered: 'success',
};

export default function VocabularyTermDetailPage() {
  const params = useParams();
  const router = useRouter();
  const termId = typeof params?.termId === 'string' ? params.termId : Array.isArray(params?.termId) ? params.termId[0] : null;

  const [term, setTerm] = useState<VocabularyTerm | null>(null);
  const [myEntry, setMyEntry] = useState<LearnerVocabulary | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  // §3A — suppress template/filler example copy so the block simply disappears
  // instead of showing a sentence that teaches nothing.
  const exampleText = useMemo(
    () => cleanExampleSentence(term?.term, term?.exampleSentence),
    [term?.term, term?.exampleSentence],
  );

  // §3B — while Practice Spelling is open this card must not reveal the answer:
  // the word, its pronunciation hint, the definition and the example are all
  // masked, leaving the audio as the only cue.
  const [spellingOpen, setSpellingOpen] = useState(false);

  useEffect(() => {
    if (!termId) return;
    analytics.track('vocab_term_detail_viewed', { termId });
    void loadAll(termId);
  }, [termId]);

  async function loadAll(id: string) {
    setLoading(true);
    try {
      const [termR, listR] = await Promise.all([
        fetchVocabularyTerm(id),
        fetchMyVocabulary(undefined, { page: 1, pageSize: 1, termId: id }).catch(() => []),
      ]);
      setTerm(termR as VocabularyTerm);
      const myList = Array.isArray(listR) ? listR : ((listR as { items?: LearnerVocabulary[] }).items ?? []);
      const mine = (myList as LearnerVocabulary[]).find(lv => lv.termId === id) ?? null;
      setMyEntry(mine);
    } catch {
      setError('Could not load term.');
    } finally {
      setLoading(false);
    }
  }

  async function handleAdd() {
    if (!term || saving || myEntry) return;
    setSaving(true);
    try {
      const res = await addToMyVocabulary(term.id, { sourceRef: 'detail' });
      analytics.track('vocab_added', { termId: term.id, source: 'detail' });
      const added = (res as { item?: LearnerVocabulary }).item;
      if (added) setMyEntry(added);
      else await loadAll(term.id);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Could not add term.';
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  async function handleRemove() {
    if (!term || saving || !myEntry) return;
    setSaving(true);
    try {
      await removeFromMyVocabulary(term.id);
      analytics.track('vocab_removed', { termId: term.id });
      setMyEntry(null);
    } catch {
      setError('Could not remove term.');
    } finally {
      setSaving(false);
    }
  }

  async function playAudio() {
    if (!term) return;
    try {
      const response = await guardAudio(() => fetchRecallsAudio(term.id, 'normal'), { termId: term.id });
      if (response) {
        playTransientAudio(response.url);
      }
    } catch {
      setError('Pronunciation audio is not ready yet.');
    }
  }

  if (loading) {
    return (
      <>
        <Skeleton className="h-36 rounded-2xl" />
        <Skeleton className="h-40 rounded-2xl" />
      </>
    );
  }

  if (!term) {
    return (
      <EmptyState
        icon={<BookOpen className="h-7 w-7" aria-hidden="true" />}
        title="Term not found."
        description={error ?? undefined}
        action={{ label: 'Go back', onClick: () => router.back() }}
      />
    );
  }

  // Free-preview locking: the backend redacts non-preview terms for
  // non-subscribed learners. Render a locked state with the canonical subscribe
  // prompt instead of the (empty) redacted content.
  if (term.isLocked) {
    return (
      <>
        <LearnerPageHero
          eyebrow="Vocabulary"
          title={term.term}
          description="Locked recall word"
          icon={Lock}
        />
        <MotionSection>
          <Card padding="lg" className="flex flex-col items-center text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Lock className="h-6 w-6" aria-hidden="true" />
            </div>
            <h2 className="mt-4 text-lg font-bold text-navy">
              Subscribe to unlock the full Recall Vocabulary Bank.
            </h2>
            <p className="mt-2 max-w-md text-sm text-muted">
              Free learners can preview a curated selection of recall words. Subscribe to reveal this term&apos;s
              definition, examples, British clinical pronunciation, and the full Recalls drill set.
            </p>
            <div className="mt-6 flex flex-col items-center justify-center gap-2 sm:flex-row">
              <Button asChild>
                <Link href="/catalog">View upgrade options</Link>
              </Button>
              <Button variant="outline" asChild>
                <Link href="/vocabulary/browse">Back to browse</Link>
              </Button>
            </div>
          </Card>
        </MotionSection>
      </>
    );
  }

  return (
    <>
      <LearnerPageHero
        eyebrow="Vocabulary"
        title={spellingOpen ? 'Listen and spell the word' : term.term}
        description={
          spellingOpen
            ? 'Practice spelling'
            : (term.ipaPronunciation ?? term.category.replace(/_/g, ' '))
        }
        icon={BookOpen}
        highlights={[
          { icon: BookOpen, label: 'Category', value: term.category.replace(/_/g, ' ') },
        ]}
        aside={(
          <Button variant="ghost" size="sm" asChild>
            <Link href="/vocabulary">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to vocabulary
            </Link>
          </Button>
        )}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}
      {audioUpgradeModal}

      <MotionSection className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="min-w-0 space-y-6 lg:col-span-2">
          {/* Definition — the hero already names the term, so this card is titled by its role. */}
          <Card padding="lg">
            <LearnerSurfaceSectionHeader title="Definition" className="mb-3" />
            <div className="flex flex-wrap items-center gap-3">
              <button
                type="button"
                onClick={() => void playAudio()}
                className="pressable inline-flex min-h-11 items-center gap-2 rounded-full bg-primary/5 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                // While practising, the accessible name must not give the answer away.
                aria-label={spellingOpen ? 'Play pronunciation' : `Play pronunciation of ${term.term}`}
              >
                <Volume2 className="h-4 w-4" aria-hidden="true" /> Play audio
              </button>
              <RecallTierBadge count={term.examFrequencyCount ?? 0} occurrences={term.recallSetOccurrences} />
            </div>
            {!spellingOpen && <p className="mt-4 text-base text-navy">{term.definition}</p>}
            {!spellingOpen && term.contextNotes && (
              <div className="mt-4 rounded-xl border border-info/20 bg-info/10 p-4 text-sm text-info">
                <div className="mb-1 eyebrow text-info">Usage notes</div>
                {term.contextNotes}
              </div>
            )}
            <PracticeSpelling
              termId={term.id}
              open={spellingOpen}
              onOpenChange={setSpellingOpen}
            />
          </Card>

          {/* Example — §3A: only rendered when the sentence actually demonstrates
              the term; template/filler copy is suppressed by the shared guard. */}
          {!spellingOpen && exampleText && (
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Example"
                title="In clinical context"
                description="How the term appears in an OET-style sentence."
                className="mb-3"
              />
              <blockquote className="rounded-lg bg-primary/5 px-4 py-2 text-base italic text-navy">
                &quot;{exampleText}&quot;
              </blockquote>
            </Card>
          )}

          {/* Synonyms / Collocations / Related */}
          {(term.synonyms?.length > 0 || term.collocations?.length > 0 || term.relatedTerms?.length > 0) && (
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Context"
                title="Synonyms, collocations, and related terms"
                description="Expand your range when speaking and writing."
                className="mb-4"
              />
              <div className="space-y-4">
                {term.synonyms?.length > 0 && (
                  <div>
                    <div className="mb-2 eyebrow text-muted">Synonyms</div>
                    <div className="flex flex-wrap gap-2">
                      {term.synonyms.map((s, i) => (
                        <span key={i} className="rounded-full bg-background-light px-3 py-1 text-sm text-navy">{s}</span>
                      ))}
                    </div>
                  </div>
                )}
                {term.collocations?.length > 0 && (
                  <div>
                    <div className="mb-2 eyebrow text-muted">Collocations</div>
                    <div className="flex flex-wrap gap-2">
                      {term.collocations.map((s, i) => (
                        <span key={i} className="rounded-full bg-info/10 px-3 py-1 text-sm text-info">{s}</span>
                      ))}
                    </div>
                  </div>
                )}
                {term.relatedTerms?.length > 0 && (
                  <div>
                    <div className="mb-2 eyebrow text-muted">Related terms</div>
                    <div className="flex flex-wrap gap-2">
                      {term.relatedTerms.map((s, i) => (
                        <span key={i} className="rounded-full border border-border bg-surface px-3 py-1 text-sm text-navy">{s}</span>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            </Card>
          )}
        </div>

        {/* Sidebar: My list card */}
        <div className="space-y-4">
          <Card padding="lg">
            <div className="mb-3 eyebrow text-muted">My word bank</div>
            {myEntry ? (
              <>
                <div className="mb-4">
                  <Badge variant={MASTERY_BADGES[myEntry.mastery] ?? 'muted'} className="capitalize">
                    {myEntry.mastery}
                  </Badge>
                  <div className="mt-3 space-y-1 text-sm text-muted">
                    <div>Review count: <span className="font-medium tabular-nums text-navy">{myEntry.reviewCount}</span></div>
                    <div>Correct: <span className="font-medium tabular-nums text-navy">{myEntry.correctCount}</span></div>
                    <div>Next review: <span className="font-medium tabular-nums text-navy">{myEntry.nextReviewDate ?? '–'}</span></div>
                    <div>Interval: <span className="font-medium tabular-nums text-navy">{myEntry.intervalDays}d</span></div>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={handleRemove}
                  disabled={saving}
                  className="pressable inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-control border border-danger/30 bg-danger/10 px-4 py-2 text-sm font-medium text-danger-strong hover:bg-danger/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-danger disabled:opacity-50"
                >
                  <Trash2 className="h-4 w-4" aria-hidden="true" /> Remove from my list
                </button>
              </>
            ) : (
              <>
                <p className="mb-4 text-sm text-muted">Save this term to track your progress with spaced repetition.</p>
                <Button onClick={handleAdd} disabled={saving} fullWidth>
                  {saving ? 'Adding…' : (<><Plus className="h-4 w-4" aria-hidden="true" /> Add to my list</>)}
                </Button>
              </>
            )}
          </Card>

          {/* Metadata card */}
          <Card padding="lg" className="text-sm">
            <div className="mb-3 eyebrow text-muted">About</div>
            <div className="space-y-2 text-muted">
              <div>Exam: <span className="font-medium text-navy">{term.examTypeCode.toUpperCase()}</span></div>
              {term.professionId && <div>Profession: <span className="font-medium text-navy capitalize">{term.professionId}</span></div>}
              <div className="flex flex-wrap items-center gap-2">Category: <CategoryBadge category={term.category} size="sm" /></div>
            </div>
          </Card>
        </div>
      </MotionSection>

      {/* Added confirmation */}
      {myEntry && (
        <div className="flex justify-center">
          <Button asChild>
            <Link href="/vocabulary/flashcards">
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> Practice with flashcards
            </Link>
          </Button>
        </div>
      )}
    </>
  );
}
