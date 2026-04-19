export type VideoLessonProgress = {
  watchedSeconds: number;
  completed: boolean;
  percentComplete: number;
  lastWatchedAt: string | null;
};

export type VideoLessonListItem = {
  id: string;
  source: 'content_hierarchy' | 'legacy_video_lesson' | string;
  examTypeCode: string;
  subtestCode: string | null;
  title: string;
  description: string | null;
  durationSeconds: number;
  thumbnailUrl: string | null;
  category: string;
  instructorName: string | null;
  difficultyLevel: string;
  isAccessible: boolean;
  isPreviewEligible: boolean;
  requiresUpgrade: boolean;
  progress: VideoLessonProgress | null;
  programId: string | null;
  moduleId: string | null;
  sortOrder: number;
};

export type VideoLessonChapter = {
  timeSeconds: number;
  title: string;
};

export type VideoLessonResource = {
  title: string;
  url: string | null;
  type: string | null;
};

export type VideoLessonDetail = VideoLessonListItem & {
  videoUrl: string | null;
  captionUrl: string | null;
  transcriptUrl: string | null;
  accessReason: string;
  mediaAssetId: string | null;
  programTitle: string | null;
  trackId: string | null;
  trackTitle: string | null;
  moduleTitle: string | null;
  previousLessonId: string | null;
  nextLessonId: string | null;
  chapters: VideoLessonChapter[];
  resources: VideoLessonResource[];
};

export type VideoLessonProgramModule = {
  id: string;
  title: string;
  description: string | null;
  estimatedDurationMinutes: number;
  lessons: VideoLessonListItem[];
};

export type VideoLessonProgramTrack = {
  id: string;
  title: string;
  description: string | null;
  subtestCode: string | null;
  modules: VideoLessonProgramModule[];
};

export type VideoLessonProgram = {
  id: string;
  title: string;
  description: string | null;
  examTypeCode: string;
  thumbnailUrl: string | null;
  isAccessible: boolean;
  tracks: VideoLessonProgramTrack[];
};

export type VideoProgressUpdateResponse = {
  completed: boolean;
  watchedSeconds: number;
  percentComplete: number;
  lastWatchedAt: string;
};
