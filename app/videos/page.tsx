'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  ArrowLeft,
  BookOpen,
  BookOpenCheck,
  ChevronRight,
  Clock,
  FolderClosed,
  Headphones,
  Mic,
  PenLine,
  PlayCircle,
  RotateCcw,
  Search,
  Video,
} from 'lucide-react';
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
import type { VideoLibraryCategory, VideoLibraryHome, VideoSummary } from '@/lib/types/videos';

type LibraryView = 'browse' | 'continue' | 'saved';
type LanguageKey = 'all' | 'en' | 'ar';

const LANGUAGE_TABS: Array<[LanguageKey, string]> = [
  ['all', 'All'],
  ['en', 'English'],
  ['ar', 'العربية'],
];

// The learner opens the module as four subtest cards (Listening · Reading · Writing ·
// Speaking). Each card drills into its collections (the "Module / Sub / …" shelves),
// and a collection drills into its videos. Order + accent theming per module below.
type ModuleTheme = {
  key: string;
  label: string;
  icon: LucideIcon;
  iconWrap: string;   // icon tile bg + text
  gradient: string;   // card background wash
  hoverBorder: string;
  accentText: string; // arrow / hover accent
};

const MODULES: ModuleTheme[] = [
  {
    key: 'listening',
    label: 'Listening',
    icon: Headphones,
    iconWrap: 'bg-sky-100 text-sky-600 dark:bg-sky-900/50 dark:text-sky-300',
    gradient: 'from-sky-50/80 to-surface dark:from-sky-950/30 dark:to-surface',
    hoverBorder: 'hover:border-sky-300 dark:hover:border-sky-700',
    accentText: 'text-sky-600 dark:text-sky-300',
  },
  {
    key: 'reading',
    label: 'Reading',
    icon: BookOpen,
    iconWrap: 'bg-emerald-100 text-emerald-600 dark:bg-emerald-900/50 dark:text-emerald-300',
    gradient: 'from-emerald-50/80 to-surface dark:from-emerald-950/30 dark:to-surface',
    hoverBorder: 'hover:border-emerald-300 dark:hover:border-emerald-700',
    accentText: 'text-emerald-600 dark:text-emerald-300',
  },
  {
    key: 'writing',
    label: 'Writing',
    icon: PenLine,
    iconWrap: 'bg-violet-100 text-violet-600 dark:bg-violet-900/50 dark:text-violet-300',
    gradient: 'from-violet-50/80 to-surface dark:from-violet-950/30 dark:to-surface',
    hoverBorder: 'hover:border-violet-300 dark:hover:border-violet-700',
    accentText: 'text-violet-600 dark:text-violet-300',
  },
  {
    key: 'speaking',
    label: 'Speaking',
    icon: Mic,
    iconWrap: 'bg-rose-100 text-rose-600 dark:bg-rose-900/50 dark:text-rose-300',
    gradient: 'from-rose-50/80 to-surface dark:from-rose-950/30 dark:to-surface',
    hoverBorder: 'hover:border-rose-300 dark:hover:border-rose-700',
    accentText: 'text-rose-600 dark:text-rose-300',
  },
];

const MODULE_BY_KEY = new Map(MODULES.map((m) => [m.key, m]));

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

function newestFirst(a: VideoSummary, b: VideoSummary) {
  return (b.publishedAt ?? '').localeCompare(a.publishedAt ?? '');
}

export default function VideoLibraryPage() {
  const [home, setHome] = useState<VideoLibraryHome | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState<LanguageKey>('all');
  const [view, setView] = useState<LibraryView>('browse');
  const [moduleKey, setModuleKey] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);

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
  const scopedAll = useMemo(() => allVideos.filter(matchesLanguage), [allVideos, matchesLanguage]);
  const continueVideos = useMemo(
    () => (home ? home.continueWatching.filter(videoHasProgress).filter(matchesLanguage) : []),
    [home, matchesLanguage],
  );
  const savedVideos = useMemo(
    () => allVideos.filter((video) => video.bookmarked).filter(matchesLanguage),
    [allVideos, matchesLanguage],
  );

  // Module → its collections (language-scoped, non-empty), grouped for the drill-down.
  const modules = useMemo(() => {
    if (!home) return [];
    const byModule = new Map<string, VideoLibraryCategory[]>();
    for (const category of home.categories) {
      const videos = language === 'all' ? category.videos : category.videos.filter(matchesLanguage);
      if (videos.length === 0) continue;
      const key = moduleKeyOf(category.title);
      const scoped = { ...category, videos };
      const bucket = byModule.get(key);
      if (bucket) bucket.push(scoped);
      else byModule.set(key, [scoped]);
    }
    return MODULES.map((meta) => {
      const categories = (byModule.get(meta.key) ?? []).sort((a, b) =>
        subTitleOf(a.title).localeCompare(subTitleOf(b.title)),
      );
      const videoCount = categories.reduce((sum, c) => sum + c.videos.length, 0);
      return { meta, categories, videoCount };
    }).filter((m) => m.videoCount > 0);
  }, [home, language, matchesLanguage]);

  const activeModule = moduleKey ? modules.find((m) => m.meta.key === moduleKey) ?? null : null;
  const activeCategory = useMemo(() => {
    if (!categoryId || !activeModule) return null;
    return activeModule.categories.find((c) => c.id === categoryId) ?? null;
  }, [activeModule, categoryId]);

  const searchQuery = query.trim().toLowerCase();
  const searchResults = useMemo(() => {
    if (!searchQuery) return [];
    return scopedAll
      .filter(
        (video) =>
          video.title.toLowerCase().includes(searchQuery) ||
          (video.description ?? '').toLowerCase().includes(searchQuery) ||
          video.tags.some((tag) => tag.toLowerCase().includes(searchQuery)),
      )
      .sort(newestFirst);
  }, [scopedAll, searchQuery]);

  const goToRoot = useCallback(() => {
    setModuleKey(null);
    setCategoryId(null);
  }, []);

  const switchView = useCallback((next: LibraryView) => {
    setView(next);
    setQuery('');
    setModuleKey(null);
    setCategoryId(null);
  }, []);

  const heroHighlights = [
    { icon: PlayCircle, label: 'Library', value: `${scopedAll.length} videos` },
    { icon: Clock, label: 'In progress', value: `${continueVideos.length} to resume` },
    { icon: BookOpenCheck, label: 'Watch on', value: 'Desktop & mobile app' },
  ];

  const grid = (videos: VideoSummary[]) => (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
      {videos.map((video, index) => (
        <MotionItem key={video.id} delayIndex={index}>
          <VideoCard video={video} onToggleBookmark={handleToggleBookmark} />
        </MotionItem>
      ))}
    </div>
  );

  const emptyCard = (title: string, hint: string, reset?: () => void) => (
    <Card className="border-dashed border-border p-8 text-center shadow-sm">
      <p className="text-sm font-semibold text-navy">{title}</p>
      <p className="mt-2 text-sm text-muted">{hint}</p>
      {reset && (
        <button
          type="button"
          onClick={reset}
          className="mt-4 inline-flex items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:border-primary hover:text-primary"
        >
          <RotateCcw className="h-4 w-4" />
          Back to library
        </button>
      )}
    </Card>
  );

  function renderBody() {
    if (loading) {
      return (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {Array.from({ length: 4 }).map((_, index) => (
            <Skeleton key={index} className="h-40 rounded-2xl" />
          ))}
        </div>
      );
    }
    if (!home || allVideos.length === 0) {
      return emptyCard('No videos published yet.', 'New video lessons will appear here as soon as they are released.');
    }

    // Search overrides the drill-down.
    if (searchQuery) {
      return (
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Search"
            title={`${searchResults.length} result${searchResults.length === 1 ? '' : 's'}`}
            description="Matching your search across every subtest."
            className="mb-4"
          />
          {searchResults.length === 0
            ? emptyCard('No videos match your search.', 'Try a different keyword or clear the search box.', () => setQuery(''))
            : grid(searchResults)}
        </section>
      );
    }

    if (view === 'continue') {
      return (
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Continue watching"
            title="Pick up where you stopped"
            description="Resume at the exact second you left — progress syncs across your devices."
            className="mb-4"
          />
          {continueVideos.length === 0
            ? emptyCard('Nothing in progress.', 'Start a lesson and it will appear here to resume.', () => switchView('browse'))
            : grid(continueVideos)}
        </section>
      );
    }

    if (view === 'saved') {
      return (
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Saved"
            title="Your saved videos"
            description="Everything you have bookmarked, in one place."
            className="mb-4"
          />
          {savedVideos.length === 0
            ? emptyCard('No saved videos yet.', 'Tap the heart on any video to save it for later.', () => switchView('browse'))
            : grid(savedVideos)}
        </section>
      );
    }

    // ── Drill-down: collection videos (level 3) ─────────────────────────────
    if (activeModule && activeCategory) {
      return (
        <section className="space-y-5">
          <Breadcrumb
            module={activeModule.meta}
            collection={subTitleOf(activeCategory.title)}
            onRoot={goToRoot}
            onModule={() => setCategoryId(null)}
          />
          {activeCategory.videos.length === 0
            ? emptyCard('No videos in this collection.', 'Try another language or collection.', goToRoot)
            : grid([...activeCategory.videos].sort(newestFirst))}
        </section>
      );
    }

    // ── Drill-down: a module's collections (level 2) ────────────────────────
    if (activeModule) {
      const Icon = activeModule.meta.icon;
      return (
        <section className="space-y-5">
          <Breadcrumb module={activeModule.meta} onRoot={goToRoot} />
          <div className="flex items-center gap-3">
            <span className={`flex h-12 w-12 items-center justify-center rounded-2xl ${activeModule.meta.iconWrap}`}>
              <Icon className="h-6 w-6" aria-hidden="true" />
            </span>
            <div>
              <h2 className="text-2xl font-bold text-navy">{activeModule.meta.label}</h2>
              <p className="text-xs text-muted">
                {activeModule.categories.length} collection{activeModule.categories.length === 1 ? '' : 's'} ·{' '}
                {activeModule.videoCount} video{activeModule.videoCount === 1 ? '' : 's'}
              </p>
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {activeModule.categories.map((category, index) => (
              <MotionItem key={category.id} delayIndex={index}>
                <button
                  type="button"
                  onClick={() => setCategoryId(category.id)}
                  className={`group flex w-full items-center justify-between gap-4 rounded-2xl border border-border bg-surface p-5 text-left shadow-sm transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-0.5 hover:shadow-clinical ${activeModule.meta.hoverBorder}`}
                >
                  <span className="flex items-center gap-4 min-w-0">
                    <span className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-xl ${activeModule.meta.iconWrap}`}>
                      <FolderClosed className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <span className="min-w-0">
                      <span className="block truncate font-semibold text-navy">{subTitleOf(category.title)}</span>
                      <span className="text-xs text-muted">
                        {category.videos.length} video{category.videos.length === 1 ? '' : 's'}
                      </span>
                    </span>
                  </span>
                  <ChevronRight
                    className={`h-5 w-5 shrink-0 text-muted transition-transform group-hover:translate-x-1 ${activeModule.meta.accentText}`}
                    aria-hidden="true"
                  />
                </button>
              </MotionItem>
            ))}
          </div>
        </section>
      );
    }

    // ── Drill-down: the four subtest cards (level 1 — landing) ──────────────
    return (
      <section className="space-y-5">
        <LearnerSurfaceSectionHeader
          eyebrow="Browse by subtest"
          title="Choose a subtest to start"
          description="Open a subtest to see its video collections, then pick a collection to watch."
          className="mb-1"
        />
        {modules.length === 0 ? (
          emptyCard('No videos for this language.', 'Switch the language filter to see more.', () => setLanguage('all'))
        ) : (
          <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
            {modules.map((mod, index) => {
              const Icon = mod.meta.icon;
              return (
                <MotionItem key={mod.meta.key} delayIndex={index}>
                  <button
                    type="button"
                    onClick={() => {
                      setModuleKey(mod.meta.key);
                      setCategoryId(null);
                    }}
                    className={`group relative flex h-full w-full flex-col overflow-hidden rounded-3xl border border-border bg-gradient-to-br ${mod.meta.gradient} p-6 text-left shadow-sm transition-[transform,box-shadow,border-color] duration-200 hover:-translate-y-1 hover:shadow-clinical ${mod.meta.hoverBorder}`}
                  >
                    <Icon
                      className="pointer-events-none absolute -bottom-6 -right-4 h-32 w-32 opacity-[0.06]"
                      aria-hidden="true"
                    />
                    <div className="flex items-center justify-between">
                      <span className={`flex h-14 w-14 items-center justify-center rounded-2xl ${mod.meta.iconWrap}`}>
                        <Icon className="h-7 w-7" aria-hidden="true" />
                      </span>
                      <ChevronRight
                        className={`h-6 w-6 text-muted transition-transform group-hover:translate-x-1 ${mod.meta.accentText}`}
                        aria-hidden="true"
                      />
                    </div>
                    <h3 className="mt-6 text-2xl font-bold text-navy">{mod.meta.label}</h3>
                    <p className="mt-1 text-sm text-muted">
                      {mod.categories.length} collection{mod.categories.length === 1 ? '' : 's'} ·{' '}
                      {mod.videoCount} video{mod.videoCount === 1 ? '' : 's'}
                    </p>
                  </button>
                </MotionItem>
              );
            })}
          </div>
        )}
      </section>
    );
  }

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

        {/* Slim toolbar: search · language · continue/saved */}
        <Card className="p-4 shadow-sm">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <label className="relative w-full lg:max-w-sm">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
              <input
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search all videos…"
                aria-label="Search videos"
                className="w-full rounded-lg border border-border bg-surface py-2.5 pl-9 pr-4 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              />
            </label>

            <div className="flex flex-wrap items-center gap-3">
              {hasLanguageTags && (
                <div
                  role="group"
                  aria-label="Filter by instruction language"
                  className="inline-flex overflow-hidden rounded-lg border border-border bg-surface text-xs font-semibold text-muted"
                >
                  {LANGUAGE_TABS.map(([key, label]) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => {
                        setLanguage(key);
                        setModuleKey(null);
                        setCategoryId(null);
                      }}
                      aria-pressed={language === key}
                      className={`px-3 py-2 transition-colors ${language === key ? 'bg-primary text-white dark:bg-violet-700' : 'hover:bg-lavender/30'}`}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              )}

              <div className="inline-flex overflow-hidden rounded-lg border border-border bg-surface text-xs font-semibold text-muted">
                {([
                  ['browse', 'Browse'],
                  ['continue', `Continue (${continueVideos.length})`],
                  ['saved', `Saved (${savedVideos.length})`],
                ] as const).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => switchView(key)}
                    aria-pressed={view === key && !searchQuery}
                    className={`px-3 py-2 transition-colors ${view === key && !searchQuery ? 'bg-primary text-white dark:bg-violet-700' : 'hover:bg-lavender/30'}`}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </Card>

        {renderBody()}
      </div>
    </LearnerDashboardShell>
  );
}

function Breadcrumb({
  module,
  collection,
  onRoot,
  onModule,
}: {
  module: ModuleTheme;
  collection?: string;
  onRoot: () => void;
  onModule?: () => void;
}) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        onClick={onRoot}
        className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-surface px-3 py-1.5 text-xs font-semibold text-navy shadow-sm transition-colors hover:border-primary hover:text-primary"
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
        All subtests
      </button>
      <nav className="flex flex-wrap items-center gap-1.5 text-sm">
        <button type="button" onClick={onRoot} className="font-medium text-muted transition-colors hover:text-primary">
          Videos
        </button>
        <ChevronRight className="h-4 w-4 text-muted/60" aria-hidden="true" />
        {collection ? (
          <>
            <button
              type="button"
              onClick={onModule}
              className="font-medium text-muted transition-colors hover:text-primary"
            >
              {module.label}
            </button>
            <ChevronRight className="h-4 w-4 text-muted/60" aria-hidden="true" />
            <span className="font-semibold text-navy">{collection}</span>
          </>
        ) : (
          <span className="font-semibold text-navy">{module.label}</span>
        )}
      </nav>
    </div>
  );
}
