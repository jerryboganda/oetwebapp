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
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import type { VideoDetail } from '@/lib/types/videos';
import { PlayerLockScreen } from '@/components/videos/player-lock-screen';
import { VideoPlayer, type VideoPlayerHandle } from '@/components/videos/video-player';

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

export default function VideoDetailPage() {
  const params = useParams();
  const { user } = useAuth();
  const rawId = params?.id;
  const videoId = Array.isArray(rawId) ? rawId[0] : rawId;

  const [video, setVideo] = useState<VideoDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const playerRef = useRef<VideoPlayerHandle | null>(null);

  // Runtime kind is stamped pre-paint on <html data-runtime-kind>, but reading
  // it during SSR is impossible — resolve after mount to avoid hydration drift.
  const [runtimeKind, setRuntimeKind] = useState<'web' | 'desktop' | 'capacitor-native' | null>(null);
  useEffect(() => {
    setRuntimeKind(getAppRuntimeKind());
  }, []);

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
        <div className="flex items-start gap-3">
          <Link href="/videos" className="mt-1 text-muted/60 hover:text-muted" aria-label="Back to video library">
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div className="min-w-0 flex-1">
            <div className="mb-2 flex flex-wrap items-center gap-2 text-xs text-muted">
              <Badge variant="outline">{SUBTEST_LABELS[video.subtestCode ?? ''] ?? 'General'}</Badge>
              {video.difficulty && <Badge variant="muted">{labelFor(video.difficulty)}</Badge>}
              <span className="inline-flex items-center gap-1 font-semibold">
                <Clock className="h-3.5 w-3.5" />
                {formatDuration(video.durationSeconds)}
              </span>
              {video.progress?.completed && <Badge variant="success">Completed</Badge>}
              {locked && <Badge variant="warning">Premium</Badge>}
            </div>
            <h1 className="text-2xl font-bold text-navy">{video.title}</h1>
          </div>
          <button
            type="button"
            onClick={handleToggleBookmark}
            aria-label={video.bookmarked ? 'Remove from saved videos' : 'Save video'}
            aria-pressed={video.bookmarked}
            className="mt-1 rounded-full border border-border p-2 text-muted hover:border-primary hover:text-primary"
          >
            <Heart className={`h-4 w-4 ${video.bookmarked ? 'fill-red-500 text-red-500' : ''}`} />
          </button>
        </div>

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
          <div className="space-y-6">
            <div className="overflow-hidden rounded-2xl bg-background-dark shadow-sm">
              <div className="aspect-video">
                {runtimeKind === null ? (
                  <Skeleton className="h-full w-full" />
                ) : runtimeKind === 'web' ? (
                  <PlayerLockScreen title={video.title} thumbnailUrl={video.thumbnailUrl} />
                ) : locked ? (
                  <div className="flex h-full flex-col items-center justify-center gap-3 px-6 text-center text-white/65">
                    <LockKeyhole className="h-16 w-16" />
                    <span className="max-w-md text-sm">
                      This video is part of a premium package. Upgrade your subscription to unlock the
                      full Video Library.
                    </span>
                    <Link
                      href="/subscriptions"
                      className="mt-2 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
                    >
                      View plans & packages
                    </Link>
                  </div>
                ) : (
                  <VideoPlayer
                    ref={playerRef}
                    videoId={video.id}
                    userId={user?.userId ?? ''}
                    durationSeconds={video.durationSeconds}
                    initialProgress={video.progress}
                    chapters={video.chapters}
                    onProgressPersisted={handleProgressPersisted}
                  />
                )}
              </div>
            </div>

            <Card className="p-5 shadow-sm">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <h2 className="font-semibold text-navy">Progress</h2>
                  <p className="mt-1 text-sm text-muted">
                    Resume at {formatDuration(video.progress?.positionSeconds ?? 0)}.
                  </p>
                </div>
                <span className="text-sm font-bold text-primary">{progress}%</span>
              </div>
              <div className="mt-4 h-2 overflow-hidden rounded-full bg-background-light">
                <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
              </div>
            </Card>

            {video.description && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-2 font-semibold text-navy">About this video</h2>
                <p className="whitespace-pre-wrap text-sm leading-6 text-muted">{video.description}</p>
              </Card>
            )}
          </div>

          <aside className="space-y-4">
            <Card className="p-5 shadow-sm">
              <h2 className="mb-3 flex items-center gap-2 font-semibold text-navy">
                <BookOpen className="h-4 w-4 text-primary" />
                Video details
              </h2>
              <div className="space-y-3 text-sm">
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Access</span>
                  <span className="font-semibold text-navy">{video.accessTier === 'free' ? 'Free' : 'Premium'}</span>
                </div>
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Difficulty</span>
                  <span className="font-semibold text-navy">{labelFor(video.difficulty)}</span>
                </div>
                {video.captions.length > 0 && (
                  <div className="flex justify-between gap-3">
                    <span className="text-muted">Captions</span>
                    <span className="font-semibold text-navy">
                      {video.captions.map((caption) => caption.label).join(', ')}
                    </span>
                  </div>
                )}
                {video.tags.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 pt-1">
                    {video.tags.map((tag) => (
                      <Badge key={tag} variant="muted">{tag}</Badge>
                    ))}
                  </div>
                )}
              </div>
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
                      disabled={runtimeKind === 'web' || locked}
                      className="flex w-full items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-left text-sm hover:border-primary hover:text-primary disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:border-border disabled:hover:text-inherit"
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
                      className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-sm hover:border-primary hover:text-primary"
                    >
                      <span>{attachment.title}</span>
                      <Download className="h-4 w-4" />
                    </a>
                  ))}
                </div>
              </Card>
            )}

            <Card className="p-5 shadow-sm">
              <h2 className="mb-3 font-semibold text-navy">Next step</h2>
              <div className="grid gap-2">
                {video.previousVideoId && (
                  <Link
                    href={`/videos/${video.previousVideoId}`}
                    className="inline-flex items-center justify-between rounded-lg border border-border px-3 py-2 text-sm font-semibold text-navy hover:border-primary hover:text-primary"
                  >
                    <span className="inline-flex items-center gap-2"><ChevronLeft className="h-4 w-4" /> Previous video</span>
                  </Link>
                )}
                {video.nextVideoId ? (
                  <Link
                    href={`/videos/${video.nextVideoId}`}
                    className="inline-flex items-center justify-between rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    <span>Next video</span>
                    <ChevronRight className="h-4 w-4" />
                  </Link>
                ) : (
                  <Link
                    href="/videos"
                    className="inline-flex items-center justify-between rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                  >
                    <span>Back to library</span>
                    <ChevronRight className="h-4 w-4" />
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
