'use client';

import Image from 'next/image';
import Link from 'next/link';
import { ChevronRight, Clock, Heart, LockKeyhole, PlayCircle, Star } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { VideoSummary } from '@/lib/types/videos';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
};

const NEW_BADGE_WINDOW_MS = 14 * 24 * 60 * 60 * 1000;

function formatDuration(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return '—';
  const minutes = Math.max(1, Math.round(seconds / 60));
  return `${minutes} min`;
}

function labelFor(value: string | null | undefined) {
  if (!value) return 'General';
  return value.replace(/[_-]/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase());
}

export function videoHasProgress(video: VideoSummary) {
  return (video.progress?.positionSeconds ?? 0) > 0 && video.progress?.completed !== true;
}

export function VideoCard({
  video,
  onToggleBookmark,
}: {
  video: VideoSummary;
  onToggleBookmark?: (videoId: string) => void;
}) {
  const progress = video.progress?.percentComplete ?? 0;
  const locked = video.requiresUpgrade && !video.isAccessible;
  const isNew =
    Boolean(video.publishedAt) &&
    Date.now() - new Date(video.publishedAt as string).getTime() < NEW_BADGE_WINDOW_MS;

  return (
    <Link href={`/videos/${video.id}`} className="group block h-full">
      <article className="flex h-full flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
        <div className="relative flex h-40 items-center justify-center bg-navy">
          {video.thumbnailUrl ? (
            <Image
              src={video.thumbnailUrl}
              alt={video.title}
              fill
              unoptimized
              sizes="(max-width: 1024px) 100vw, 33vw"
              className="object-cover"
            />
          ) : (
            <PlayCircle className="h-14 w-14 text-white/70 transition-colors group-hover:text-white" />
          )}
          <div className="absolute left-3 top-3 flex flex-wrap gap-2">
            {locked ? (
              <Badge variant="warning"><LockKeyhole className="mr-1 h-3 w-3" /> Premium</Badge>
            ) : video.progress?.completed ? (
              <Badge variant="success">Completed</Badge>
            ) : video.accessTier === 'free' ? (
              <Badge variant="info">Free</Badge>
            ) : null}
            {isNew && <Badge variant="info">New</Badge>}
            {video.isFeatured && (
              <Badge variant="muted"><Star className="mr-1 h-3 w-3" /> Featured</Badge>
            )}
          </div>
          {onToggleBookmark && (
            <button
              type="button"
              aria-label={video.bookmarked ? 'Remove from saved videos' : 'Save video'}
              aria-pressed={video.bookmarked}
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onToggleBookmark(video.id);
              }}
              className="absolute right-3 top-3 rounded-full bg-navy/70 p-2 text-white transition-colors hover:bg-navy"
            >
              <Heart className={`h-4 w-4 ${video.bookmarked ? 'fill-red-400 text-red-400' : ''}`} />
            </button>
          )}
          <div className="absolute bottom-3 right-3 flex items-center gap-1 rounded-full bg-navy/70 px-2 py-1 text-xs text-white">
            <Clock className="h-3 w-3" />
            {formatDuration(video.durationSeconds)}
          </div>
        </div>

        <div className="flex flex-1 flex-col p-5">
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <Badge variant="muted">{SUBTEST_LABELS[video.subtestCode ?? ''] ?? 'General'}</Badge>
            {video.difficulty && <span className="text-xs font-semibold text-muted">{labelFor(video.difficulty)}</span>}
          </div>
          <h3 className="line-clamp-2 text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">
            {video.title}
          </h3>
          {video.description && (
            <p className="mt-2 line-clamp-2 text-xs leading-5 text-muted">{video.description}</p>
          )}

          <div className="mt-auto pt-4">
            <div className="h-2 overflow-hidden rounded-full bg-background-light">
              <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
            </div>
            <div className="mt-3 flex items-center justify-between text-xs font-semibold">
              <span className="text-muted">{progress}% complete</span>
              <span className="inline-flex items-center gap-1 text-primary">
                {locked ? 'View details' : videoHasProgress(video) ? 'Resume' : 'Watch'}
                <ChevronRight className="h-3.5 w-3.5" />
              </span>
            </div>
          </div>
        </div>
      </article>
    </Link>
  );
}
