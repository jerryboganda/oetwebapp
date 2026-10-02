'use client';

import Image from 'next/image';
import { ChevronRight, Clock, Heart, LockKeyhole, PlayCircle, Star } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { CardLink } from '@/components/ui/card-link';
import { ProgressBar } from '@/components/ui/progress';
import type { VideoSummary } from '@/lib/types/videos';

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
  const subtest = video.subtestCode ?? '';

  return (
    <CardLink href={`/videos/${video.id}`} padding="none" className="group flex h-full flex-col overflow-hidden">
      <article className="flex h-full flex-col">
        <div className="relative flex h-40 items-center justify-center bg-background-dark">
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
            <PlayCircle className="h-14 w-14 text-white/70 transition-colors group-hover:text-white" aria-hidden="true" />
          )}
          <div className="absolute start-3 top-3 flex flex-wrap gap-2">
            {locked ? (
              <Badge variant="warning"><LockKeyhole className="me-1 h-3 w-3" aria-hidden="true" /> Premium</Badge>
            ) : video.progress?.completed ? (
              <Badge variant="success">Completed</Badge>
            ) : video.accessTier === 'free' ? (
              <Badge variant="info">Free</Badge>
            ) : null}
            {isNew && <Badge variant="info">New</Badge>}
            {video.isFeatured && (
              <Badge variant="muted"><Star className="me-1 h-3 w-3" aria-hidden="true" /> Featured</Badge>
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
              className="absolute end-2 top-2 flex h-11 w-11 items-center justify-center rounded-full bg-background-dark/70 text-white transition-colors hover:bg-background-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white"
            >
              <Heart className={`h-4 w-4 ${video.bookmarked ? 'fill-danger text-danger-strong' : ''}`} aria-hidden="true" />
            </button>
          )}
          <div className="absolute bottom-3 end-3 flex items-center gap-1 rounded-full bg-background-dark/70 px-2 py-1 text-xs tabular-nums text-white">
            <Clock className="h-3 w-3" aria-hidden="true" />
            {formatDuration(video.durationSeconds)}
          </div>
        </div>

        <div className="flex flex-1 flex-col p-5">
          <div className="mb-2 flex flex-wrap items-center gap-2">
            <Badge variant="muted" className={SUBTEST_CHIPS[subtest]}>{SUBTEST_LABELS[subtest] ?? 'General'}</Badge>
            {video.language && (
              <Badge variant="info">{video.language === 'ar' ? 'Arabic' : 'English'}</Badge>
            )}
            {video.difficulty && <span className="text-xs font-semibold text-muted">{labelFor(video.difficulty)}</span>}
          </div>
          <h3 className="line-clamp-2 text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">
            {video.title}
          </h3>
          {video.description && (
            <p className="mt-2 line-clamp-2 text-xs leading-5 text-muted">{video.description}</p>
          )}

          <div className="mt-auto pt-4">
            <ProgressBar value={progress} ariaLabel={`${progress}% complete`} />
            <div className="mt-3 flex items-center justify-between text-xs font-semibold">
              <span className="tabular-nums text-muted">{progress}% complete</span>
              <span className="inline-flex items-center gap-1 text-primary">
                {locked ? 'View details' : videoHasProgress(video) ? 'Resume' : 'Watch'}
                <ChevronRight className="h-3.5 w-3.5 transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden="true" />
              </span>
            </div>
          </div>
        </div>
      </article>
    </CardLink>
  );
}
