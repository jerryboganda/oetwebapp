'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BookOpen, BookOpenCheck, Clock, Headphones, Mic, PenLine, PlayCircle, RotateCcw, Search, Video } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { fetchVideoLibraryHome, toggleVideoBookmark } from '@/lib/api/videos';
import { analytics } from '@/lib/analytics';
import { VideoCard, videoHasProgress } from '@/components/videos/video-card';
import type { VideoLibraryHome, VideoSummary } from '@/lib/types/videos';

type LibraryView = 'all' | 'continue' | 'saved';
type SortKey = 'newest' | 'title' | 'duration' | 'popular';
type LanguageKey = 'all' | 'en' | 'ar';

// English is one shared set across every profession; Arabic sets are profession-scoped.
// This filter lets a learner narrow the whole library to English or Arabic instruction.
const LANGUAGE_TABS: Array<[LanguageKey, string]> = [
  ['all', 'All languages'],
  ['en', 'English'],
  ['ar', 'العربية'],
];

const SUBTEST_OPTIONS: Array<[string, string]> = [
  ['listening', 'Listening'],
  ['reading', 'Reading'],
  ['writing', 'Writing'],
  ['speaking', 'Speaking'],
];

// Category shelves are named "Module / Sub / Sub" (e.g. "Reading / English / Sessions").
// The learner browse view groups them into top-level module sections, each holding
// its sub-category shelves — so the tree reads Module → subcategory → videos.
const MODULE_ORDER: Record<string, number> = { listening: 0, reading: 1, speaking: 2, writing: 3, mocks: 4 };
const MODULE_META: Record<string, { label: string; icon: LucideIcon }> = {
  listening: { label: 'Listening', icon: Headphones },
  reading: { label: 'Reading', icon: BookOpen },
  speaking: { label: 'Speaking', icon: Mic },
  writing: { label: 'Writing', icon: PenLine },
  mocks: { label: 'Mocks', icon: BookOpenCheck },
};

function moduleKeyOf(title: string): string {
  return (title.split('/')[0] ?? '').trim().toLowerCase();
}

function subTitleOf(title: string): string {
  const parts = title.split('/').map((part) => part.trim()).filter(Boolean);
  return parts.slice(1).join(' / ') || 'General';
}

function flattenVideos(home: VideoLibraryHome): VideoSummary[] {
  const byId = new Map<string, VideoSummary>();
  for (const list of [home.featured, home.continueWatching, home.uncategorized, ...home.categories.map((c) => c.videos)]) {
    for (const video of list) {
      if (!byId.has(video.id)) byId.set(video.id, video);
    }
  }
  return Array.from(byId.values());
}

function applyBookmark(home: VideoLibraryHome, videoId: string, bookmarked: boolean): VideoLibraryHome {
  const patch = (videos: VideoSummary[]) =>
    videos.map((video) => (video.id === videoId ? { ...video, bookmarked } : video));
  return {
    featured: patch(home.featured),
    continueWatching: patch(home.continueWatching),
    uncategorized: patch(home.uncategorized),
    categories: home.categories.map((category) => ({ ...category, videos: patch(category.videos) })),
  };
}

export default function VideoLibraryPage() {
  const [home, setHome] = useState<VideoLibraryHome | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [subtest, setSubtest] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [sort, setSort] = useState<SortKey>('newest');
  const [view, setView] = useState<LibraryView>('all');
  const [language, setLanguage] = useState<LanguageKey>('all');

  useEffect(() => {
    analytics.track('videos_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      setLoading(true);
      setError(null);
      try {
        const data = await fetchVideoLibraryHome();
        if (!cancelled) setHome(data);
      } catch {
        if (!cancelled) {
          setHome(null);
          setError('Could not load the video library.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const handleToggleBookmark = useCallback((videoId: string) => {
    setHome((current) => {
      if (!current) return current;
      const target = flattenVideos(current).find((video) => video.id === videoId);
      const next = !(target?.bookmarked ?? false);
      void toggleVideoBookmark(videoId).catch(() => {
        // Roll back on failure.
        setHome((rollback) => (rollback ? applyBookmark(rollback, videoId, !next) : rollback));
      });
      return applyBookmark(current, videoId, next);
    });
  }, []);

  const matchesLanguage = useCallback(
    (video: VideoSummary) => language === 'all' || video.language === language,
    [language],
  );

  const allVideos = useMemo(() => (home ? flattenVideos(home) : []), [home]);
  const languageCounts = useMemo(
    () => ({
      en: allVideos.filter((video) => video.language === 'en').length,
      ar: allVideos.filter((video) => video.language === 'ar').length,
    }),
    [allVideos],
  );
  const hasLanguageTags = languageCounts.en > 0 || languageCounts.ar > 0;
  const continueVideos = useMemo(
    () => (home ? home.continueWatching.filter(videoHasProgress).filter(matchesLanguage) : []),
    [home, matchesLanguage],
  );
  const featuredVideos = useMemo(
    () => (home ? home.featured.filter(matchesLanguage) : []),
    [home, matchesLanguage],
  );
  const savedVideos = useMemo(
    () => allVideos.filter((video) => video.bookmarked).filter(matchesLanguage),
    [allVideos, matchesLanguage],
  );
  // Language-scoped total, so every surfaced count (hero + "All" tab) matches
  // what the active language filter actually renders.
  const scopedAll = useMemo(() => allVideos.filter(matchesLanguage), [allVideos, matchesLanguage]);

  // Group the flat category shelves into top-level module sections (Reading, Listening,
  // Speaking, Writing …) so the default browse view renders as a Module → subcategory tree.
  const moduleGroups = useMemo(() => {
    if (!home) return [];
    const groups = new Map<string, VideoLibraryHome['categories']>();
    for (const category of home.categories) {
      const videos = language === 'all' ? category.videos : category.videos.filter(matchesLanguage);
      if (videos.length === 0) continue;
      const scoped = { ...category, videos };
      const key = moduleKeyOf(category.title);
      const bucket = groups.get(key);
      if (bucket) bucket.push(scoped);
      else groups.set(key, [scoped]);
    }
    return Array.from(groups.entries())
      .sort((a, b) => (MODULE_ORDER[a[0]] ?? 99) - (MODULE_ORDER[b[0]] ?? 99) || a[0].localeCompare(b[0]))
      .map(([key, categories]) => ({
        key,
        meta: MODULE_META[key] ?? { label: key ? key[0].toUpperCase() + key.slice(1) : 'Other', icon: Video },
        categories,
        count: categories.reduce((sum, category) => sum + category.videos.length, 0),
      }));
  }, [home, language, matchesLanguage]);

  // A language selection is an active filter too: it routes to the flat results
  // path (which language-filters every video, incl. uncategorized) and gets the
  // shared empty-state / reset card — so no English/null videos leak into an
  // Arabic view, and an empty language never renders a blank page.
  const filtersActive =
    Boolean(query.trim() || subtest || categoryId) || view !== 'all' || sort !== 'newest' || language !== 'all';

  const visibleVideos = useMemo(() => {
    let list = view === 'continue' ? continueVideos : view === 'saved' ? savedVideos : allVideos;
    const q = query.trim().toLowerCase();
    if (q) {
      list = list.filter(
        (video) =>
          video.title.toLowerCase().includes(q) ||
          (video.description ?? '').toLowerCase().includes(q) ||
          video.tags.some((tag) => tag.toLowerCase().includes(q)),
      );
    }
    if (subtest) list = list.filter((video) => video.subtestCode === subtest);
    if (categoryId) list = list.filter((video) => video.categoryIds.includes(categoryId));
    if (language !== 'all') list = list.filter(matchesLanguage);
    const sorted = [...list];
    if (sort === 'title') sorted.sort((a, b) => a.title.localeCompare(b.title));
    else if (sort === 'duration') sorted.sort((a, b) => a.durationSeconds - b.durationSeconds);
    else if (sort === 'popular') sorted.sort((a, b) => b.viewCount - a.viewCount);
    else sorted.sort((a, b) => (b.publishedAt ?? '').localeCompare(a.publishedAt ?? ''));
    return sorted;
  }, [allVideos, categoryId, continueVideos, language, matchesLanguage, query, savedVideos, sort, subtest, view]);

  const heroHighlights = [
    { icon: PlayCircle, label: 'Library', value: `${scopedAll.length} videos` },
    { icon: Clock, label: 'In progress', value: `${continueVideos.length} to resume` },
    { icon: BookOpenCheck, label: 'Watch on', value: 'Desktop & mobile app' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Video Library"
          title="Learn from expert-led OET video lessons"
          description="Structured video teaching for every subtest — with resume, chapters, captions, and downloadable handouts. Playback is available in the OET desktop and mobile apps."
          icon={Video}
          highlights={heroHighlights}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <Card className="p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="Find a video"
            title="Search and filter the library"
            description="Filter by subtest or category, search titles and tags, and sort the results."
            className="mb-4"
          />
          <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
            <div className="flex flex-wrap items-center gap-3">
              <label className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
                <input
                  type="search"
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Search videos…"
                  aria-label="Search videos"
                  className="rounded-lg border border-border bg-surface py-3 pl-9 pr-4 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
                />
              </label>
              <select
                value={subtest}
                onChange={(event) => setSubtest(event.target.value)}
                aria-label="Filter by subtest"
                className="rounded-lg border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <option value="">All subtests</option>
                {SUBTEST_OPTIONS.map(([value, label]) => (
                  <option key={value} value={value}>{label}</option>
                ))}
              </select>
              <select
                value={categoryId}
                onChange={(event) => setCategoryId(event.target.value)}
                aria-label="Filter by category"
                className="rounded-lg border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <option value="">All categories</option>
                {(home?.categories ?? []).map((category) => (
                  <option key={category.id} value={category.id}>{category.title}</option>
                ))}
              </select>
              <select
                value={sort}
                onChange={(event) => setSort(event.target.value as SortKey)}
                aria-label="Sort videos"
                className="rounded-lg border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <option value="newest">Newest first</option>
                <option value="title">A – Z</option>
                <option value="duration">Shortest first</option>
                <option value="popular">Most watched</option>
              </select>

              {hasLanguageTags && (
                <div
                  role="group"
                  aria-label="Filter by instruction language"
                  className="inline-flex overflow-hidden rounded-lg border border-border bg-surface text-xs font-semibold text-muted"
                >
                  {LANGUAGE_TABS.map(([key, label]) => {
                    const count = key === 'en' ? languageCounts.en : key === 'ar' ? languageCounts.ar : null;
                    return (
                      <button
                        key={key}
                        type="button"
                        onClick={() => setLanguage(key)}
                        aria-pressed={language === key}
                        className={`px-3 py-3 transition-colors ${language === key ? 'bg-primary text-white dark:bg-violet-700' : 'hover:bg-lavender/30'}`}
                      >
                        {label}
                        {count !== null ? ` (${count})` : ''}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="grid grid-cols-3 overflow-hidden rounded-lg border border-border bg-surface text-xs font-semibold text-muted">
              {([
                ['all', `All (${scopedAll.length})`],
                ['continue', `Continue (${continueVideos.length})`],
                ['saved', `Saved (${savedVideos.length})`],
              ] as const).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => setView(key)}
                  className={`px-3 py-2 transition-colors ${view === key ? 'bg-primary text-white dark:bg-violet-700' : 'hover:bg-lavender/30'}`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
        </Card>

        {loading ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => (
              <Skeleton key={index} className="h-72 rounded-2xl" />
            ))}
          </div>
        ) : !home || allVideos.length === 0 ? (
          <Card className="border-dashed border-border p-8 text-center shadow-sm">
            <p className="text-sm font-semibold text-navy">No videos published yet.</p>
            <p className="mt-2 text-sm text-muted">New video lessons will appear here as soon as they are released.</p>
          </Card>
        ) : filtersActive ? (
          visibleVideos.length === 0 ? (
            <Card className="border-dashed border-border p-8 text-center shadow-sm">
              <p className="text-sm font-semibold text-navy">No videos match this view.</p>
              <p className="mt-2 text-sm text-muted">Try clearing the search or one of the filters.</p>
              <button
                type="button"
                onClick={() => {
                  setQuery('');
                  setSubtest('');
                  setCategoryId('');
                  setSort('newest');
                  setView('all');
                  setLanguage('all');
                }}
                className="mt-4 inline-flex items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:border-primary hover:text-primary"
              >
                <RotateCcw className="h-4 w-4" />
                Reset filters
              </button>
            </Card>
          ) : (
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow={view === 'continue' ? 'Continue watching' : view === 'saved' ? 'Saved videos' : 'Results'}
                title={view === 'continue' ? 'Pick up where you stopped' : view === 'saved' ? 'Your saved videos' : `${visibleVideos.length} videos`}
                description="Progress and completion sync across your devices."
                className="mb-4"
              />
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                {visibleVideos.map((video, index) => (
                  <MotionItem key={video.id} delayIndex={index}>
                    <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
                  </MotionItem>
                ))}
              </div>
            </section>
          )
        ) : (
          <div className="space-y-8">
            {continueVideos.length > 0 && (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Continue watching"
                  title="Pick up where you stopped"
                  description="Your most recent videos, ready to resume at the exact second you left."
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                  {continueVideos.slice(0, 6).map((video, index) => (
                    <MotionItem key={video.id} delayIndex={index}>
                      <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
                    </MotionItem>
                  ))}
                </div>
              </section>
            )}

            {featuredVideos.length > 0 && (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="Featured"
                  title="Hand-picked by Dr Hesham"
                  description="Start here — the highest-impact lessons in the library."
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                  {featuredVideos.map((video, index) => (
                    <MotionItem key={video.id} delayIndex={index}>
                      <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
                    </MotionItem>
                  ))}
                </div>
              </section>
            )}

            {moduleGroups.map((group) => {
              const ModuleIcon = group.meta.icon;
              return (
                <section key={group.key} className="space-y-6">
                  <div className="flex items-center gap-3 border-b border-border pb-3">
                    <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-lavender/40 text-primary dark:bg-violet-900/40">
                      <ModuleIcon className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <div>
                      <h2 className="text-xl font-bold text-navy">{group.meta.label}</h2>
                      <p className="text-xs text-muted">
                        {group.count} video{group.count === 1 ? '' : 's'}
                      </p>
                    </div>
                  </div>
                  <div className="space-y-8 md:pl-2">
                    {group.categories.map((category) => (
                      <div key={category.id}>
                        <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">
                          {subTitleOf(category.title)}
                        </h3>
                        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                          {category.videos.map((video, index) => (
                            <MotionItem key={video.id} delayIndex={index}>
                              <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
                            </MotionItem>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                </section>
              );
            })}

            {home.uncategorized.length > 0 && (
              <section>
                <LearnerSurfaceSectionHeader
                  eyebrow="More videos"
                  title="Everything else"
                  className="mb-4"
                />
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
                  {home.uncategorized.map((video, index) => (
                    <MotionItem key={video.id} delayIndex={index}>
                      <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
                    </MotionItem>
                  ))}
                </div>
              </section>
            )}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
