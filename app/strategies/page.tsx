'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import {
  ArrowRight,
  BookOpenCheck,
  Bookmark,
  CheckCircle2,
  ChevronRight,
  Clock,
  Lightbulb,
  Lock,
  Search,
  Sparkles,
  Target,
} from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerDashboardShell } from '@/components/layout';
import { Badge, Card, InlineAlert, MotionItem, MotionSection, ProgressBar, Skeleton } from '@/components/ui';
import { fetchStrategyGuides, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import type { StrategyGuideLibrary, StrategyGuideListItem } from '@/lib/types/strategies';

const SUBTEST_FILTERS = [
  { value: '', label: 'All subtests' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
];

const DEFAULT_CATEGORIES = [
  { code: '', label: 'All categories' },
  { code: 'overview', label: 'Overview' },
  { code: 'subtest_strategy', label: 'Subtest strategy' },
  { code: 'time_management', label: 'Time management' },
  { code: 'common_mistakes', label: 'Common mistakes' },
  { code: 'exam_day', label: 'Exam day' },
];

function formatLabel(value: string | null | undefined) {
  if (!value) return 'General';
  return value
    .split(/[_-]/g)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function progressLabel(guide: StrategyGuideListItem) {
  if (guide.progress.completed) return 'Completed';
  if (guide.progress.readPercent > 0) return `${guide.progress.readPercent}% read`;
  return 'Not started';
}

function guideStatus(guide: StrategyGuideListItem) {
  if (guide.progress.completed) return { label: 'Read', variant: 'success' as const };
  if (guide.bookmarked) return { label: 'Saved', variant: 'info' as const };
  if (!guide.isAccessible && guide.isPreviewEligible) return { label: 'Preview', variant: 'warning' as const };
  if (!guide.isAccessible && guide.requiresUpgrade) return { label: 'Locked', variant: 'muted' as const };
  return { label: 'Guide', variant: 'outline' as const };
}

function StrategyCard({ guide, compact = false }: { guide: StrategyGuideListItem; compact?: boolean }) {
  const status = guideStatus(guide);
  const locked = !guide.isAccessible && guide.requiresUpgrade && !guide.isPreviewEligible;

  return (
    <Link href={`/strategies/${encodeURIComponent(guide.id)}`} className="block h-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 rounded-2xl">
      <Card hoverable className={cn('h-full transition-transform hover:-translate-y-0.5', locked && 'opacity-85')}>
        <div className="flex h-full flex-col justify-between gap-5">
          <div className="space-y-4">
            <div className="flex items-start justify-between gap-3">
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <Badge variant={status.variant}>{status.label}</Badge>
                <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-muted">
                  <Clock className="h-3.5 w-3.5" />
                  {guide.readingTimeMinutes} min
                </span>
              </div>
              {locked ? <Lock className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" /> : null}
              {!locked && guide.bookmarked ? <Bookmark className="h-4 w-4 shrink-0 fill-primary text-primary" aria-hidden="true" /> : null}
            </div>

            <div>
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-primary">
                {formatLabel(guide.subtestCode)} / {formatLabel(guide.category)}
              </p>
              <h3 className={cn('mt-2 font-bold text-navy', compact ? 'text-base' : 'text-lg')}>{guide.title}</h3>
              {guide.summary ? <p className="mt-2 text-sm leading-6 text-muted">{guide.summary}</p> : null}
            </div>

            {guide.recommendedReason ? (
              <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                <Sparkles className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <span>{guide.recommendedReason}</span>
              </div>
            ) : null}

            <ProgressBar value={guide.progress.readPercent} showValue ariaLabel={`${guide.title} reading progress`} color={guide.progress.completed ? 'success' : 'primary'} />
          </div>

          <div className="flex items-center justify-between gap-3 border-t border-border pt-4 text-sm font-semibold text-navy">
            <span>{progressLabel(guide)}</span>
            <span className="inline-flex items-center gap-1 text-primary">
              {locked ? 'View access' : guide.isPreviewEligible && !guide.isAccessible ? 'Preview' : 'Open'}
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </span>
          </div>
        </div>
      </Card>
    </Link>
  );
}

function GuideSection({
  title,
  description,
  icon,
  guides,
  empty,
  compact,
}: {
  title: string;
  description?: string;
  icon: typeof Lightbulb;
  guides: StrategyGuideListItem[];
  empty: string;
  compact?: boolean;
}) {
  const Icon = icon;

  return (
    <MotionSection className="space-y-4">
      <LearnerSurfaceSectionHeader title={title} description={description} icon={Icon} />
      {guides.length > 0 ? (
        <div className={cn('grid gap-4', compact ? 'md:grid-cols-2 xl:grid-cols-3' : 'lg:grid-cols-2 xl:grid-cols-3')}>
          {guides.map((guide, index) => (
            <MotionItem key={guide.id} delayIndex={index}>
              <StrategyCard guide={guide} compact={compact} />
            </MotionItem>
          ))}
        </div>
      ) : (
        <Card className="border-dashed bg-background-light text-sm text-muted">{empty}</Card>
      )}
    </MotionSection>
  );
}

function LoadingState() {
  return (
    <div className="space-y-5">
      <Skeleton className="h-36 rounded-[24px]" />
      <div className="grid gap-4 lg:grid-cols-3">
        {[0, 1, 2].map((item) => (
          <Skeleton key={item} className="h-64 rounded-2xl" />
        ))}
      </div>
    </div>
  );
}

function DisabledState() {
  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Strategy guides are being prepared"
        description="The guided OET strategy library is not available to learners yet. Your study plan, lessons, and practice tasks are still ready."
        icon={Lightbulb}
        accent="amber"
        highlights={[
          { label: 'Status', value: 'Coming soon', icon: Sparkles },
          { label: 'Access', value: 'Learner only', icon: Lock },
        ]}
      />
      <Card className="mt-6 border-dashed bg-background-light">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-lg font-bold text-navy">Use practice modules while this opens</h2>
            <p className="mt-1 text-sm leading-6 text-muted">Recommended strategies will appear here once the release flag is enabled.</p>
          </div>
          <Link href="/dashboard" className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-primary/90">
            Back to dashboard
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        </div>
      </Card>
    </LearnerDashboardShell>
  );
}

export default function StrategiesPage() {
  const [library, setLibrary] = useState<StrategyGuideLibrary | null>(null);
  const [subtestCode, setSubtestCode] = useState('');
  const [category, setCategory] = useState('');
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [disabled, setDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('strategies_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;

    async function loadGuides() {
      setLoading(true);
      setError(null);
      setDisabled(false);

      try {
        const data = await fetchStrategyGuides({
          examTypeCode: 'oet',
          subtestCode: subtestCode || undefined,
          category: category || undefined,
          q: query.trim() || undefined,
        });

        if (!cancelled) {
          setLibrary(data);
        }
      } catch (err) {
        if (cancelled) return;
        if (isApiError(err) && err.code === 'FEATURE_DISABLED') {
          setDisabled(true);
          setLibrary(null);
          return;
        }

        setError(isApiError(err) ? err.userMessage : 'Unable to load strategy guides right now.');
        setLibrary(null);
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadGuides();

    return () => {
      cancelled = true;
    };
  }, [category, query, subtestCode]);

  const categoryOptions = useMemo(() => {
    const seeded = new Map(DEFAULT_CATEGORIES.map((item) => [item.code, item]));
    for (const item of library?.categories ?? []) {
      if (!seeded.has(item.code)) {
        seeded.set(item.code, { code: item.code, label: formatLabel(item.code) });
      }
    }
    return Array.from(seeded.values());
  }, [library?.categories]);

  if (disabled) {
    return <DisabledState />;
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-7">
        <LearnerPageHero
          eyebrow="Strategy Library"
          title="Use the right strategy at the right moment"
          description="Practical reading, listening, writing, and speaking strategy articles matched to your weak subtests and study progress."
          icon={Lightbulb}
          accent="amber"
          highlights={[
            { label: 'Guides', value: loading ? 'Loading' : `${library?.items.length ?? 0} available`, icon: BookOpenCheck },
            { label: 'Recommended', value: `${library?.recommended.length ?? 0} for you`, icon: Target },
            { label: 'Saved', value: `${library?.bookmarked.length ?? 0} bookmarks`, icon: Bookmark },
          ]}
        />

        <Card className="space-y-4">
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_180px_210px]">
            <label className="relative block">
              <span className="sr-only">Search strategy guides</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search by skill, timing, case notes, roleplay..."
                className="min-h-11 w-full rounded-lg border border-border bg-background px-10 py-2.5 text-sm text-navy outline-none transition-colors placeholder:text-muted focus:border-primary focus:ring-2 focus:ring-primary/20"
              />
            </label>

            <label>
              <span className="sr-only">Filter by subtest</span>
              <select
                value={subtestCode}
                onChange={(event) => setSubtestCode(event.target.value)}
                className="min-h-11 w-full rounded-lg border border-border bg-background px-3 py-2.5 text-sm font-semibold text-navy outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
              >
                {SUBTEST_FILTERS.map((item) => (
                  <option key={item.value} value={item.value}>{item.label}</option>
                ))}
              </select>
            </label>

            <label>
              <span className="sr-only">Filter by category</span>
              <select
                value={category}
                onChange={(event) => setCategory(event.target.value)}
                className="min-h-11 w-full rounded-lg border border-border bg-background px-3 py-2.5 text-sm font-semibold text-navy outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
              >
                {categoryOptions.map((item) => (
                  <option key={item.code} value={item.code}>{item.label}</option>
                ))}
              </select>
            </label>
          </div>
        </Card>

        {error ? (
          <InlineAlert variant="error" title="Strategy guides did not load">{error}</InlineAlert>
        ) : null}

        {loading ? (
          <LoadingState />
        ) : library ? (
          <div className="space-y-8">
            <GuideSection
              title="Recommended Next"
              description="Matched to your OET focus areas and high-impact guide order."
              icon={Sparkles}
              guides={library.recommended}
              empty="No recommendations match the current filters yet."
            />

            <GuideSection
              title="Continue Reading"
              description="Guides you have started but not finished."
              icon={Clock}
              guides={library.continueReading}
              empty="Start any guide and it will appear here."
              compact
            />

            <GuideSection
              title="Bookmarked"
              description="Saved strategy guides for quick review."
              icon={Bookmark}
              guides={library.bookmarked}
              empty="Bookmark a guide to keep it close during revision."
              compact
            />

            <GuideSection
              title="All Strategy Guides"
              description="Searchable OET strategy articles for every subtest."
              icon={CheckCircle2}
              guides={library.items}
              empty="No strategy guides match these filters. Try another subtest or clear the search."
            />
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
