'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ArrowLeft,
  BookOpen,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Download,
  Heart,
  LockKeyhole,
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { useAuth } from '@/contexts/auth-context';
import { analytics } from '@/lib/analytics';
import { fetchVideo, toggleVideoBookmark } from '@/lib/api/videos';
import type { VideoDetail } from '@/lib/types/videos';
import { VideoPlayer, type VideoPlayerHandle } from '@/components/videos/video-player';
import { useLowBandwidthMode } from '@/hooks/use-media-preferences';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
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
      <LearnerDashboardShell>
        <div className="space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="aspect-video rounded-2xl" />
          <Skeleton className="h-40 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!video) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Video not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const locked = video.requiresUpgrade && !video.isAccessible;
  const progress = video.progress?.percentComplete ?? 0;

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <div className="space-y-2.5">
          <div className="flex items-center justify-between gap-3">
            <Link
              href="/videos"
              aria-label="Back to video library"
              className="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full border border-border bg-surface px-3 text-[13px] font-semibold text-muted shadow-sm transition-colors hover:border-primary/40 hover:text-navy"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              Library
            </Link>
            <button
              type="button"
              onClick={handleToggleBookmark}
              aria-label={video.bookmarked ? 'Remove from saved videos' : 'Save video'}
              aria-pressed={video.bookmarked}
              className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-full border border-border bg-surface text-muted shadow-sm transition-colors hover:border-primary/40 hover:text-primary"
            >
              <Heart className={`h-4 w-4 ${video.bookmarked ? 'fill-red-500 text-red-500' : ''}`} aria-hidden="true" />
            </button>
          </div>
          <h1 className="text-xl font-bold leading-snug text-navy sm:text-2xl">{video.title}</h1>
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
            <Badge variant="outline">{SUBTEST_LABELS[video.subtestCode ?? ''] ?? 'General'}</Badge>
            {video.difficulty && <Badge variant="muted">{labelFor(video.difficulty)}</Badge>}
            <span className="inline-flex items-center gap-1 font-semibold">
              <Clock className="h-3.5 w-3.5" aria-hidden="true" />
              {formatDuration(video.durationSeconds)}
            </span>
            {video.progress?.completed && <Badge variant="success">Completed</Badge>}
            {locked && <Badge variant="warning">Premium</Badge>}
          </div>
        </div>

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
          <div className="space-y-6">
            {locked ? (
              <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-[#141428] via-[#1c1638] to-[#141428] shadow-sm">
                <div
                  className="pointer-events-none absolute -right-16 -top-16 h-56 w-56 rounded-full bg-primary/25 blur-3xl"
                  aria-hidden="true"
                />
                <div
                  className="pointer-events-none absolute -bottom-20 -left-12 h-48 w-48 rounded-full bg-violet-500/15 blur-3xl"
                  aria-hidden="true"
                />
                <div className="relative flex flex-col items-center gap-4 px-6 py-10 text-center sm:py-12">
                  <span className="flex h-14 w-14 items-center justify-center rounded-2xl border border-white/15 bg-white/10 text-white shadow-inner">
                    <LockKeyhole className="h-7 w-7" aria-hidden="true" />
                  </span>
                  <div className="space-y-1.5">
                    <p className="text-[11px] font-bold uppercase tracking-[0.22em] text-violet-300">
                      Premium lesson
                    </p>
                    <h2 className="text-lg font-bold text-white">Unlock the full Video Library</h2>
                    <p className="mx-auto max-w-sm text-sm leading-relaxed text-white/60">
                      This workshop is part of a premium package. Upgrade once and every recorded
                      lesson, handout and workshop opens up.
                    </p>
                  </div>
                  <Link
                    href="/subscriptions"
                    className="mt-1 inline-flex items-center gap-2 rounded-xl bg-primary px-5 py-2.5 text-sm font-semibold text-white shadow-lg shadow-primary/30 transition-colors hover:bg-primary-dark"
                  >
                    View plans &amp; packages
                    <ChevronRight className="h-4 w-4" aria-hidden="true" />
                  </Link>
                </div>
              </div>
            ) : (
              <div className="overflow-hidden rounded-2xl bg-background-dark shadow-sm">
                <div className="aspect-video">
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
              <Card className="p-5 shadow-sm">
                <div className="flex items-center justify-between gap-4">
                  <div>
                    <h2 className="font-semibold text-navy">Progress</h2>
                    <p className="mt-1 text-sm text-muted">
                      {progress > 0
                        ? `Resume at ${formatDuration(video.progress?.positionSeconds ?? 0)}.`
                        : 'Not started yet — press play to begin.'}
                    </p>
                  </div>
                  <span className="text-sm font-bold text-primary">{progress}%</span>
                </div>
                <div className="mt-4 h-2 overflow-hidden rounded-full bg-background-light">
                  <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
                </div>
              </Card>
            )}

            {video.description && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-2 font-semibold text-navy">About this video</h2>
                <p className="whitespace-pre-wrap text-sm leading-6 text-muted">{video.description}</p>
              </Card>
            )}
          </div>

          <aside className="space-y-4">
            <Card className="p-5 shadow-sm">
              <h2 className="mb-1 flex items-center gap-2 font-semibold text-navy">
                <BookOpen className="h-4 w-4 text-primary" />
                Video details
              </h2>
              <dl className="divide-y divide-border/60 text-sm">
                <div className="flex items-center justify-between gap-3 py-2.5">
                  <dt className="text-muted">Access</dt>
                  <dd>
                    <span
                      className={`rounded-full px-2.5 py-0.5 text-xs font-bold ${
                        video.accessTier === 'free'
                          ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-100'
                          : 'bg-amber-100 text-amber-900 dark:bg-amber-900/50 dark:text-amber-100'
                      }`}
                    >
                      {video.accessTier === 'free' ? 'Free' : 'Premium'}
                    </span>
                  </dd>
                </div>
                <div className="flex items-center justify-between gap-3 py-2.5">
                  <dt className="text-muted">Difficulty</dt>
                  <dd className="font-semibold text-navy">{labelFor(video.difficulty)}</dd>
                </div>
                <div className="flex items-center justify-between gap-3 py-2.5">
                  <dt className="text-muted">Duration</dt>
                  <dd className="font-semibold text-navy">{formatDuration(video.durationSeconds)}</dd>
                </div>
                {video.captions.length > 0 && (
                  <div className="flex items-center justify-between gap-3 py-2.5">
                    <dt className="text-muted">Captions</dt>
                    <dd className="text-right font-semibold text-navy">
                      {video.captions.map((caption) => caption.label).join(', ')}
                    </dd>
                  </div>
                )}
              </dl>
              {video.tags.length > 0 && (
                <div className="mt-1 border-t border-border/60 pt-3">
                  <p className="mb-2 text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Topics</p>
                  <div className="flex flex-wrap gap-1.5">
                    {video.tags.map((tag) => (
                      <span
                        key={tag}
                        className="rounded-full bg-primary/8 px-2.5 py-1 text-xs font-semibold text-primary-dark ring-1 ring-primary/15 dark:bg-primary/15 dark:text-primary"
                      >
                        {formatTag(tag)}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </Card>

            {video.chapters.length > 0 && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-3 font-semibold text-navy">Chapters</h2>
                <div className="space-y-2">
                  {video.chapters.map((chapter) => (
                    <button
                      key={`${chapter.timeSeconds}-${chapter.title}`}
                      type="button"
                      onClick={() => playerRef.current?.seekTo(chapter.timeSeconds)}
                      disabled={locked}
                      className="flex w-full items-center justify-between gap-3 rounded-xl border border-border px-3.5 py-2.5 text-left text-sm font-medium text-navy transition-colors hover:border-primary/40 hover:text-primary disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:border-border disabled:hover:text-inherit"
                    >
                      <span>{chapter.title}</span>
                      <span className="font-mono text-xs text-muted">{formatDuration(chapter.timeSeconds)}</span>
                    </button>
                  ))}
                </div>
              </Card>
            )}

            {video.attachments.length > 0 && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-3 font-semibold text-navy">Handouts & resources</h2>
                <div className="space-y-2">
                  {video.attachments.map((attachment) => (
                    <a
                      key={attachment.id}
                      href={attachment.url}
                      target="_blank"
                      rel="noreferrer"
                      className="flex items-center justify-between gap-3 rounded-xl border border-border px-3.5 py-2.5 text-sm font-medium text-navy transition-colors hover:border-primary/40 hover:text-primary"
                    >
                      <span>{attachment.title}</span>
                      <Download className="h-4 w-4" />
                    </a>
                  ))}
                </div>
              </Card>
            )}

            <Card className="p-5 shadow-sm">
              <h2 className="mb-3 font-semibold text-navy">Keep watching</h2>
              <div className="grid gap-2">
                {video.nextVideoId ? (
                  <Link
                    href={`/videos/${video.nextVideoId}`}
                    className="inline-flex items-center justify-between gap-2 rounded-xl bg-primary px-4 py-2.5 text-sm font-semibold text-white shadow-sm shadow-primary/25 transition-colors hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    <span>Next video</span>
                    <ChevronRight className="h-4 w-4" aria-hidden="true" />
                  </Link>
                ) : (
                  <Link
                    href="/videos"
                    className="inline-flex items-center justify-between gap-2 rounded-xl bg-primary px-4 py-2.5 text-sm font-semibold text-white shadow-sm shadow-primary/25 transition-colors hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    <span>Back to library</span>
                    <ChevronRight className="h-4 w-4" aria-hidden="true" />
                  </Link>
                )}
                {video.previousVideoId && (
                  <Link
                    href={`/videos/${video.previousVideoId}`}
                    className="inline-flex items-center justify-between gap-2 rounded-xl border border-border bg-surface px-4 py-2.5 text-sm font-semibold text-muted transition-colors hover:border-primary/40 hover:text-navy"
                  >
                    <span className="inline-flex items-center gap-2">
                      <ChevronLeft className="h-4 w-4" aria-hidden="true" />
                      Previous video
                    </span>
                  </Link>
                )}
              </div>
            </Card>

            {video.progress?.completed && (
              <InlineAlert variant="success" className="text-sm">
                <span className="inline-flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4" />
                  Completed videos count as learning activity.
                </span>
              </InlineAlert>
            )}
          </aside>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
