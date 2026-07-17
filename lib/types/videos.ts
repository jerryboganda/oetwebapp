/**
 * Learner-facing Video Library types.
 *
 * Mirrors the backend contracts served by `VideoLibraryEndpoints.cs`
 * (`/v1/video-library/*`). Playback URLs are NEVER present on catalog or
 * detail DTOs — they are only issued through an attested playback session
 * (see `lib/video/attestation.ts`), which is what enforces the
 * native-app-only playback rule.
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

export type PlaybackSession = {
  sessionId: string;
  playbackUrl: string;
  expiresAt: string;
  watermarkText: string;
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
