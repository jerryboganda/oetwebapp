/**
 * Publish-readiness for a Video Library video, consolidating the checks the
 * review step and the publish gate surface.
 *
 * HARD rules (block publish):
 *   - encode ready
 *   - title set (not the draft seed)
 *   - at least one category assigned
 *   - a thumbnail is available (custom set OR the encode is ready, which
 *     yields Bunny's auto thumbnail)
 *   - access tier set
 *
 * SOFT rules (advisory quality checks):
 *   - description at least 20 characters
 *   - at least one caption track
 *   - at least two chapters
 *   - tags present
 */

import { VIDEO_DRAFT_SEED_TITLE } from './video-wizard-config';
import type { AdminVideoDetail } from '@/lib/api/video-library';

export interface VideoReadinessItem {
  label: string;
  ok: boolean;
  /** Hard rules block publish; soft rules are advisory. */
  hard: boolean;
}

export interface VideoReadiness {
  items: VideoReadinessItem[];
  /** True when every hard rule passes — i.e. the video can be published. */
  hardReady: boolean;
}

function filled(value: string | null | undefined, seed?: string): boolean {
  const v = (value ?? '').trim();
  if (!v) return false;
  if (seed && v === seed) return false;
  return true;
}

export function getVideoReadiness(video: AdminVideoDetail): VideoReadiness {
  const encodeReady = video.encodeStatus === 'ready';
  const thumbnailAvailable = Boolean(video.customThumbnailAssetId) || encodeReady;

  const items: VideoReadinessItem[] = [
    { label: 'Video uploaded and encoded (ready)', ok: encodeReady, hard: true },
    { label: 'Title set', ok: filled(video.title, VIDEO_DRAFT_SEED_TITLE), hard: true },
    { label: 'At least one category assigned', ok: (video.categoryIds ?? []).length >= 1, hard: true },
    { label: 'Thumbnail available (custom or auto)', ok: thumbnailAvailable, hard: true },
    {
      label: 'Access tier set',
      ok: video.accessTier === 'free' || video.accessTier === 'premium',
      hard: true,
    },
    { label: 'Description written (20+ characters)', ok: (video.description ?? '').trim().length >= 20, hard: false },
    { label: 'At least one caption track', ok: (video.captions ?? []).length >= 1, hard: false },
    { label: 'At least two chapters', ok: (video.chapters ?? []).length >= 2, hard: false },
    { label: 'Tags added', ok: filled(video.tagsCsv), hard: false },
  ];

  const hardReady = items.filter((i) => i.hard).every((i) => i.ok);
  return { items, hardReady };
}
