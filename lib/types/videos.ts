/**
 * Learner-facing Video Library types.
 *
 * Mirrors the backend contracts served by `VideoLibraryEndpoints.cs`
 * (`/v1/video-library/*`). Playback URLs are NEVER present on catalog or
 * detail DTOs — they are issued only through the protected playback-session
 * flow (see `lib/video/attestation.ts`).
 */

export type VideoAccessTier = 'free' | 'premium';

/** Instruction language of a video. `null` = unspecified (legacy content). */
export type VideoLanguage = 'en' | 'ar';

export type VideoLibraryProgress = {
  positionSeconds: number;
  percentComplete: number;
  completed: boolean;
};

export type VideoSummary = {
  id: string;
  title: string;
  description: string | null;
  durationSeconds: number;
  thumbnailUrl: string | null;
  accessTier: VideoAccessTier;
  isAccessible: boolean;
  requiresUpgrade: boolean;
  lockReason: 'subscription_required' | null;
  subtestCode: string | null;
  difficulty: string | null;
  language: VideoLanguage | null;
  tags: string[];
  isFeatured: boolean;
  publishedAt: string | null;
  viewCount: number;
  progress: VideoLibraryProgress | null;
  bookmarked: boolean;
  categoryIds: string[];
};

export type VideoChapter = {
  timeSeconds: number;
  title: string;
};

export type VideoCaptionInfo = {
  languageCode: string;
  label: string;
};

export type VideoAttachmentInfo = {
  id: string;
  title: string;
  url: string;
};

export type VideoDetail = VideoSummary & {
  chapters: VideoChapter[];
  captions: VideoCaptionInfo[];
  attachments: VideoAttachmentInfo[];
  previousVideoId: string | null;
  nextVideoId: string | null;
};

export type VideoLibraryCategory = {
  id: string;
  title: string;
  slug: string;
  description: string | null;
  videos: VideoSummary[];
};

export type VideoLibraryHome = {
  featured: VideoSummary[];
  continueWatching: VideoSummary[];
  categories: VideoLibraryCategory[];
  uncategorized: VideoSummary[];
};

export type VideoProgressResponse = {
  positionSeconds: number;
  watchedSeconds: number;
  percentComplete: number;
  completed: boolean;
};

export type PlaybackChallenge = {
  nonce: string;
  expiresAt: string;
};

export type PlaybackWatermark = {
  fullName: string;
  maskedEmail: string;
  userRef: string;
  sessionRef: string;
  platform: string;
  issuedAt: string;
};

export type PlaybackSession = {
  sessionId: string;
  playbackUrl: string;
  /** Optional only while an older API replica drains during deployment. */
  deliveryMode?: 'secure_embed' | 'direct_hls';
  /** Short-lived embed-view token expiry. */
  expiresAt: string;
  /** Server-side playback-session expiry; renewed URLs never outlive this. */
  sessionExpiresAt?: string;
  /** @deprecated kept for one deploy cycle in case of frontend/backend skew; use `watermark`. */
  watermarkText: string;
  /** Null only during the brief window where the backend hasn't deployed the
   * structured watermark yet — callers should fall back to `watermarkText`. */
  watermark: PlaybackWatermark | null;
  captions: VideoCaptionInfo[];
};

export type VideoPlaybackEventType =
  | 'play'
  | 'pause'
  | 'seek'
  | 'heartbeat'
  | 'complete'
  | 'error'
  | 'quality_changed'
  | 'session_renewed';
