'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  BookOpen,
  ChevronLeft,
  ChevronRight,
  Clock,
  Download,
  Heart,
  LockKeyhole,
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchVideo, toggleVideoBookmark } from '@/lib/api/videos';
import type { VideoDetail } from '@/lib/types/videos';
import { VideoPlayer, type VideoPlayerHandle } from '@/components/videos/video-player';
import { useLowBandwidthMode } from '@/hooks/use-media-preferences';
import { getAppRuntimeKind } from '@/lib/runtime-signals';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
};

// Sub-test identity chips (DESIGN.md §2); anything else stays a neutral chip.
const SUBTEST_CHIPS: Record<string, string> = {
  writing: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking',
  reading: 'border-skill-reading/20 bg-skill-reading/10 text-skill-reading',
  listening: 'border-skill-listening/20 bg-skill-listening/10 text-skill-listening',
};

function formatDuration(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return '0:00';
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = seconds % 60;
  return `${minutes}:${remainingSeconds.toString().padStart(2, '0')}`;
}

function labelFor(value: string | null | undefined) {
  if (!value) return 'General';
  return value.replace(/[_-]/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase());
}

/** Admin tags arrive as `key:value` pairs (e.g. `batch:new-medicine-crash-course`).
 *  Learners only care about the value, humanized. */
function formatTag(tag: string) {
  const separatorIndex = tag.indexOf(':');
  const value = separatorIndex >= 0 ? tag.slice(separatorIndex + 1) : tag;
  return labelFor(value);
}

export default function VideoDetailPage() {
  const params = useParams();
  const { user } = useAuth();
  const rawId = params?.id;
  const videoId = Array.isArray(rawId) ? rawId[0] : rawId;

  const [video, setVideo] = useState<VideoDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const playerRef = useRef<VideoPlayerHandle | null>(null);
  const lowBandwidth = useLowBandwidthMode();
  // The "app required" panel (WebNotAllowedNotice) needs more height than a
  // 16:9 crop allows on narrow phones — see the aspect-video toggle below.
  // Derived synchronously (not via a child callback + effect): VideoPlayer's
  // own boot effect gates on this exact same runtime check before it ever
  // calls the API, so computing it here directly means this render already
  // agrees with the child's about-to-happen WEB_NOT_ALLOWED phase — no frame
  // where the wrapper is still aspect-video-cropped while the ~650px notice
  // is already mounted inside it.
  const playbackBlocked = getAppRuntimeKind() === 'web';

  useEffect(() => {
    if (!videoId) return;
    let cancelled = false;

    void (async () => {
      setLoading(true);
      setError(null);
      setVideo(null);
      try {
        const data = await fetchVideo(videoId);
        if (cancelled) return;
        setVideo(data);
        analytics.track('video_detail_viewed', { videoId: data.id });
      } catch {
        if (!cancelled) {
          setVideo(null);
          setError('Could not load this video.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [videoId]);

  const handleToggleBookmark = useCallback(() => {
    setVideo((current) => {
      if (!current) return current;
      const next = !current.bookmarked;
      void toggleVideoBookmark(current.id).catch(() => {
        setVideo((rollback) => (rollback ? { ...rollback, bookmarked: !next } : rollback));
      });
      return { ...current, bookmarked: next };
    });
  }, []);

  const handleProgressPersisted = useCallback(
    (progress: { percentComplete: number; completed: boolean; positionSeconds: number }) => {
      setVideo((current) =>
        current
          ? {
              ...current,
              progress: {
                positionSeconds: progress.positionSeconds,
                percentComplete: progress.percentComplete,
                completed: progress.completed,
              },
            }
          : current,
      );
    },
    [],
  );

  if (loading) {
    return (
      <>
        <Skeleton className="h-8 w-48" />
        <Skeleton className="aspect-video rounded-2xl" />
        <Skeleton className="h-40 rounded-2xl" />
      </>
    );
  }

  if (!video) {
    return (
      <>
        <InlineAlert
          variant="warning"
          action={
            <Button asChild size="sm" variant="outline">
              <Link href="/videos">Back to library</Link>
            </Button>
          }
        >
          {error ?? 'Video not found.'}
        </InlineAlert>
      </>
    );
  }

  const locked = video.requiresUpgrade && !video.isAccessible;
  const progress = video.progress?.percentComplete ?? 0;

  return (
    <>
      {/* Detail page: one compact h1 block above the player. */}
      <div className="space-y-2.5">
        <div className="flex items-center justify-between gap-3">
          <Button variant="outline" size="sm" asChild className="rounded-full bg-surface">
            <Link href="/videos" aria-label="Back to video library">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Library
            </Link>
          </Button>
          <button
            type="button"
            onClick={handleToggleBookmark}
            aria-label={video.bookmarked ? 'Remove from saved videos' : 'Save video'}
            aria-pressed={video.bookmarked}
            className="pressable inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-full border border-border bg-surface text-muted shadow-sm transition-colors hover:border-primary/40 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <Heart className={`h-4 w-4 ${video.bookmarked ? 'fill-danger text-danger-strong' : ''}`} aria-hidden="true" />
          </button>
        </div>
        <h1 className="text-xl font-bold leading-snug text-navy sm:text-2xl">{video.title}</h1>
        <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
          <Badge variant="outline" className={SUBTEST_CHIPS[video.subtestCode ?? '']}>{SUBTEST_LABELS[video.subtestCode ?? ''] ?? 'General'}</Badge>
          {video.difficulty && <Badge variant="muted">{labelFor(video.difficulty)}</Badge>}
          <span className="inline-flex items-center gap-1 font-semibold tabular-nums">
            <Clock className="h-3.5 w-3.5" aria-hidden="true" />
            {formatDuration(video.durationSeconds)}
          </span>
          {video.progress?.completed && <Badge variant="success">Completed</Badge>}
          {locked && <Badge variant="warning">Premium</Badge>}
        </div>
      </div>

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      <MotionSection className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="min-w-0 space-y-6">
          {locked ? (
            <div className="overflow-hidden rounded-2xl bg-background-dark shadow-sm">
              <div className="flex flex-col items-center gap-4 px-6 py-10 text-center sm:py-12">
                <span className="flex h-14 w-14 items-center justify-center rounded-2xl border border-white/15 bg-white/10 text-white">
                  <LockKeyhole className="h-7 w-7" aria-hidden="true" />
                </span>
                <div className="space-y-1.5">
                  <p className="eyebrow text-primary-300">
                    Premium lesson
                  </p>
                  <h2 className="text-lg font-bold text-white">Unlock the full Video Library</h2>
                  <p className="mx-auto max-w-sm text-sm leading-relaxed text-white/70">
                    This workshop is part of a premium package. Upgrade once and every recorded
                    lesson, handout and workshop opens up.
                  </p>
                </div>
                <Button asChild className="mt-1">
                  <Link href="/subscriptions">
                    View plans &amp; packages
                    <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                  </Link>
                </Button>
              </div>
            </div>
          ) : (
              <div className="overflow-hidden rounded-2xl bg-background-dark shadow-sm">
                {/* aspect-video only while the real player is up — the "app required"
                    state (WebNotAllowedNotice, rendered by VideoPlayer itself) needs to
                    grow taller than 16:9 on narrow phones instead of being cropped by a
                    fixed-ratio, overflow-hidden box. */}
                <div className={playbackBlocked ? undefined : 'aspect-video'}>
                  <VideoPlayer
                    ref={playerRef}
                    videoId={video.id}
                    userId={user?.userId ?? ''}
                    durationSeconds={video.durationSeconds}
                    initialProgress={video.progress}
                    chapters={video.chapters}
                    onProgressPersisted={handleProgressPersisted}
                    lowBandwidth={lowBandwidth}
                  />
                </div>
              </div>
            )}

          {!locked && (
            <Card className="p-5">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <h2 className="font-semibold text-navy">Progress</h2>
                  <p className="mt-1 text-sm text-muted">
                    {progress > 0
                      ? `Resume at ${formatDuration(video.progress?.positionSeconds ?? 0)}.`
                      : 'Not started yet — press play to begin.'}
                  </p>
                </div>
                <span className="text-sm font-bold tabular-nums text-primary">{progress}%</span>
              </div>
              <ProgressBar value={progress} ariaLabel={`${progress}%`} className="mt-4" />
            </Card>
          )}

          {video.description && (
            <Card className="p-5">
              <h2 className="mb-2 font-semibold text-navy">About this video</h2>
              <p className="max-w-prose whitespace-pre-wrap text-sm leading-6 text-muted">{video.description}</p>
            </Card>
          )}
        </div>

        <aside className="space-y-4">
          <Card className="p-5">
            <h2 className="mb-1 flex items-center gap-2 font-semibold text-navy">
              <BookOpen className="h-4 w-4 text-primary" aria-hidden="true" />
              Video details
            </h2>
            <dl className="divide-y divide-border/60 text-sm">
              <div className="flex items-center justify-between gap-3 py-2.5">
                <dt className="text-muted">Access</dt>
                <dd>
                  <Badge variant={video.accessTier === 'free' ? 'success' : 'warning'}>
                    {video.accessTier === 'free' ? 'Free' : 'Premium'}
                  </Badge>
                </dd>
              </div>
              <div className="flex items-center justify-between gap-3 py-2.5">
                <dt className="text-muted">Difficulty</dt>
                <dd className="font-semibold text-navy">{labelFor(video.difficulty)}</dd>
              </div>
              <div className="flex items-center justify-between gap-3 py-2.5">
                <dt className="text-muted">Duration</dt>
                <dd className="font-semibold tabular-nums text-navy">{formatDuration(video.durationSeconds)}</dd>
              </div>
              {video.captions.length > 0 && (
                <div className="flex items-center justify-between gap-3 py-2.5">
                  <dt className="text-muted">Captions</dt>
                  <dd className="text-end font-semibold text-navy">
                    {video.captions.map((caption) => caption.label).join(', ')}
                  </dd>
                </div>
              )}
            </dl>
            {video.tags.length > 0 && (
              <div className="mt-1 border-t border-border/60 pt-3">
                <p className="mb-2 eyebrow text-muted">Topics</p>
                <div className="flex flex-wrap gap-1.5">
                  {video.tags.map((tag) => (
                    <Badge key={tag} variant="default">{formatTag(tag)}</Badge>
                  ))}
                </div>
              </div>
            )}
          </Card>

          {video.chapters.length > 0 && (
            <Card className="p-5">
              <h2 className="mb-3 font-semibold text-navy">Chapters</h2>
              <div className="space-y-2">
                {video.chapters.map((chapter) => (
                  <button
                    key={`${chapter.timeSeconds}-${chapter.title}`}
                    type="button"
                    onClick={() => playerRef.current?.seekTo(chapter.timeSeconds)}
                    disabled={locked}
                    className="flex min-h-11 w-full items-center justify-between gap-3 rounded-xl border border-border px-3.5 py-2.5 text-start text-sm font-medium text-navy transition-colors hover:border-primary/40 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:border-border disabled:hover:text-inherit"
                  >
                    <span>{chapter.title}</span>
                    <span className="font-mono text-xs tabular-nums text-muted">{formatDuration(chapter.timeSeconds)}</span>
                  </button>
                ))}
              </div>
            </Card>
          )}

          {video.attachments.length > 0 && (
            <Card className="p-5">
              <h2 className="mb-3 font-semibold text-navy">Handouts & resources</h2>
              <div className="space-y-2">
                {video.attachments.map((attachment) => (
                  <a
                    key={attachment.id}
                    href={attachment.url}
                    target="_blank"
                    rel="noreferrer"
                    className="flex min-h-11 items-center justify-between gap-3 rounded-xl border border-border px-3.5 py-2.5 text-sm font-medium text-navy transition-colors hover:border-primary/40 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  >
                    <span>{attachment.title}</span>
                    <Download className="h-4 w-4 shrink-0" aria-hidden="true" />
                  </a>
                ))}
              </div>
            </Card>
          )}

          <Card className="p-5">
            <h2 className="mb-3 font-semibold text-navy">Keep watching</h2>
            <div className="grid grid-cols-1 gap-2">
              {video.nextVideoId ? (
                <Button asChild fullWidth className="justify-between">
                  <Link href={`/videos/${video.nextVideoId}`}>
                    <span>Next video</span>
                    <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                  </Link>
                </Button>
              ) : (
                <Button asChild fullWidth className="justify-between">
                  <Link href="/videos">
                    <span>Back to library</span>
                    <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                  </Link>
                </Button>
              )}
              {video.previousVideoId && (
                <Button asChild variant="outline" fullWidth className="justify-start bg-surface">
                  <Link href={`/videos/${video.previousVideoId}`}>
                    <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                    Previous video
                  </Link>
                </Button>
              )}
            </div>
          </Card>

          {video.progress?.completed && (
            <InlineAlert variant="success" live="polite">
              Completed videos count as learning activity.
            </InlineAlert>
          )}
        </aside>
      </MotionSection>
    </>
  );
}
