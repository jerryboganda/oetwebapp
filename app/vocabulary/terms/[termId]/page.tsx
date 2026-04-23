'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Volume2, Plus, CheckCircle2, Trash2, Flame } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchVocabularyTerm,
  fetchMyVocabulary,
  addToMyVocabulary,
  removeFromMyVocabulary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VocabularyTerm, LearnerVocabulary } from '@/lib/types/vocabulary';

const MASTERY_COLORS: Record<string, string> = {
  new: 'bg-background-light text-navy',
  learning: 'bg-info/10 text-info',
  reviewing: 'bg-warning/10 text-warning',
  mastered: 'bg-success/10 text-success',
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
        fetchMyVocabulary().catch(() => []),
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

  function playAudio() {
    if (!term?.audioUrl) return;
    try { void new Audio(term.audioUrl).play(); } catch {/* ignore */}
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="mb-6 flex items-center gap-3">
          <Link href="/vocabulary/browse" className="text-muted hover:text-navy">
            <ArrowLeft className="w-5 h-5" />
          </Link>
          <Skeleton className="h-8 w-48 rounded" />
        </div>
        <Skeleton className="h-40 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!term) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning" className="mb-4">Term not found.</InlineAlert>
        <button onClick={() => router.back()} className="rounded-xl bg-primary px-4 py-2 text-sm text-white">Go back</button>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero
          eyebrow="Vocabulary"
          title={term.term}
          description={term.ipaPronunciation ?? term.category.replace(/_/g, ' ')}
          icon={BookOpen}
          highlights={[
            { icon: Flame, label: 'Difficulty', value: term.difficulty },
            { icon: BookOpen, label: 'Category', value: term.category.replace(/_/g, ' ') },
          ]}
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          {/* Definition */}
          <Card className="border-border bg-surface p-6">
            <LearnerSurfaceSectionHeader
              eyebrow="Definition"
              title={term.term}
              description={term.ipaPronunciation ?? undefined}
              className="mb-3"
            />
            <div className="flex flex-wrap items-center gap-3">
              {term.audioUrl && (
                <button
                  onClick={playAudio}
                  className="inline-flex items-center gap-2 rounded-full bg-primary/5 px-3 py-1.5 text-sm font-medium text-primary hover:bg-primary/10"
                  aria-label={`Play pronunciation of ${term.term}`}
                >
                  <Volume2 className="h-4 w-4" /> Play audio
                </button>
              )}
            </div>
            <p className="mt-4 text-base text-navy">{term.definition}</p>
            {term.contextNotes && (
              <div className="mt-4 rounded-2xl bg-info/10 p-4 text-sm text-info">
                <div className="mb-1 text-xs font-semibold uppercase text-info">Usage notes</div>
                {term.contextNotes}
              </div>
            )}
          </Card>

          {/* Example */}
          {term.exampleSentence && (
            <Card className="border-border bg-surface p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Example"
                title="In clinical context"
                description="How the term appears in an OET-style sentence."
                className="mb-3"
              />
              <blockquote className="border-l-4 border-primary/40 pl-4 text-base italic text-navy">
                &quot;{term.exampleSentence}&quot;
              </blockquote>
            </Card>
          )}

          {/* Synonyms / Collocations / Related */}
          {(term.synonyms?.length > 0 || term.collocations?.length > 0 || term.relatedTerms?.length > 0) && (
            <Card className="border-border bg-surface p-6">
              <LearnerSurfaceSectionHeader
                eyebrow="Context"
                title="Synonyms, collocations, and related terms"
                description="Expand your range when speaking and writing."
                className="mb-4"
              />
              <div className="space-y-4">
                {term.synonyms?.length > 0 && (
                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase text-muted">Synonyms</div>
                    <div className="flex flex-wrap gap-2">
                      {term.synonyms.map((s, i) => (
                        <span key={i} className="rounded-full bg-background-light px-3 py-1 text-sm text-navy">{s}</span>
                      ))}
                    </div>
                  </div>
                )}
                {term.collocations?.length > 0 && (
                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase text-muted">Collocations</div>
                    <div className="flex flex-wrap gap-2">
                      {term.collocations.map((s, i) => (
                        <span key={i} className="rounded-full bg-info/10 px-3 py-1 text-sm text-info">{s}</span>
                      ))}
                    </div>
                  </div>
                )}
                {term.relatedTerms?.length > 0 && (
                  <div>
                    <div className="mb-2 text-xs font-semibold uppercase text-muted">Related terms</div>
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
          <Card className="border-border bg-surface p-6">
            <div className="mb-3 text-xs font-semibold uppercase text-muted">My word bank</div>
            {myEntry ? (
              <>
                <div className="mb-4">
                  <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium capitalize ${MASTERY_COLORS[myEntry.mastery] ?? ''}`}>
                    {myEntry.mastery}
                  </span>
                  <div className="mt-3 space-y-1 text-sm text-muted">
                    <div>Review count: <span className="font-medium text-navy">{myEntry.reviewCount}</span></div>
                    <div>Correct: <span className="font-medium text-navy">{myEntry.correctCount}</span></div>
                    <div>Next review: <span className="font-medium text-navy">{myEntry.nextReviewDate ?? '—'}</span></div>
                    <div>Interval: <span className="font-medium text-navy">{myEntry.intervalDays}d</span></div>
                  </div>
                </div>
                <button
                  onClick={handleRemove}
                  disabled={saving}
                  className="inline-flex w-full items-center justify-center gap-2 rounded-xl border border-danger/30 bg-danger/10 px-4 py-2 text-sm font-medium text-danger hover:bg-danger/20 disabled:opacity-50"
                >
                  <Trash2 className="h-4 w-4" /> Remove from my list
                </button>
              </>
            ) : (
              <>
                <p className="mb-4 text-sm text-muted">Save this term to track your progress with spaced repetition.</p>
                <button
                  onClick={handleAdd}
                  disabled={saving}
                  className="inline-flex w-full items-center justify-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-primary/90 disabled:opacity-50"
                >
                  {saving ? 'Adding…' : (<><Plus className="h-4 w-4" /> Add to my list</>)}
                </button>
              </>
            )}
          </Card>

          {/* Metadata card */}
          <Card className="border-border bg-surface p-6 text-sm">
            <div className="mb-3 text-xs font-semibold uppercase text-muted">About</div>
            <div className="space-y-2 text-muted">
              <div>Exam: <span className="font-medium text-navy">{term.examTypeCode.toUpperCase()}</span></div>
              {term.professionId && <div>Profession: <span className="font-medium text-navy capitalize">{term.professionId}</span></div>}
              <div>Category: <span className="font-medium text-navy capitalize">{term.category.replace(/_/g, ' ')}</span></div>
              <div>Difficulty: <span className="font-medium text-navy capitalize">{term.difficulty}</span></div>
            </div>
          </Card>
        </div>
      </div>

      {/* Added confirmation */}
      {myEntry && (
        <div className="mt-6 flex justify-center">
          <Link href="/vocabulary/flashcards" className="inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
            <CheckCircle2 className="h-4 w-4" /> Practice with flashcards
          </Link>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
