export type StrategyGuideProgress = {
  readPercent: number;
  completed: boolean;
  startedAt: string | null;
  lastReadAt: string | null;
  completedAt: string | null;
  bookmarked: boolean;
  bookmarkedAt: string | null;
};

export type StrategyGuideCategory = {
  code: string;
  label: string;
  count: number;
};

export type StrategyGuideListItem = {
  id: string;
  slug: string | null;
  source: 'content_hierarchy' | 'legacy_strategy_guide' | string;
  examTypeCode: string;
  subtestCode: string | null;
  title: string;
  summary: string | null;
  category: string;
  readingTimeMinutes: number;
  isAccessible: boolean;
  isPreviewEligible: boolean;
  requiresUpgrade: boolean;
  accessReason: string;
  progress: StrategyGuideProgress;
  bookmarked: boolean;
  recommendedReason: string | null;
  programId: string | null;
  moduleId: string | null;
  contentLessonId: string | null;
  sortOrder: number;
  publishedAt: string | null;
};

export type StrategyGuideLibrary = {
  items: StrategyGuideListItem[];
  recommended: StrategyGuideListItem[];
  continueReading: StrategyGuideListItem[];
  bookmarked: StrategyGuideListItem[];
  categories: StrategyGuideCategory[];
};

export type StrategyGuideStructuredSection = {
  heading?: string;
  body?: string;
  bullets?: string[];
};

export type StrategyGuideStructuredContent = {
  version?: number;
  overview?: string;
  sections?: StrategyGuideStructuredSection[];
  keyTakeaways?: string[];
};

export type StrategyGuideDetail = StrategyGuideListItem & {
  contentJson: string | null;
  contentHtml: string | null;
  sourceProvenance: string | null;
  programTitle: string | null;
  trackId: string | null;
  trackTitle: string | null;
  moduleTitle: string | null;
  previousGuideId: string | null;
  nextGuideId: string | null;
  relatedGuides: StrategyGuideListItem[];
};

export type StrategyGuideProgressUpdateResponse = {
  progress: StrategyGuideProgress;
};

export type StrategyGuideBookmarkUpdateResponse = {
  progress: StrategyGuideProgress;
};

export type StrategyGuidePublishValidationError = {
  field: string;
  message: string;
};

export type StrategyGuidePublishValidation = {
  canPublish: boolean;
  errors: StrategyGuidePublishValidationError[];
};

export type StrategyGuideAdminItem = {
  id: string;
  slug: string | null;
  examTypeCode: string;
  subtestCode: string | null;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: number;
  sortOrder: number;
  status: string;
  isPreviewEligible: boolean;
  contentLessonId: string | null;
  contentJson: string | null;
  contentHtml: string | null;
  sourceProvenance: string | null;
  rightsStatus: string | null;
  freshnessConfidence: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
};

export type StrategyGuideUpsertPayload = {
  slug?: string | null;
  examTypeCode: string;
  subtestCode?: string | null;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: number;
  sortOrder: number;
  isPreviewEligible: boolean;
  contentLessonId?: string | null;
  contentJson?: string | null;
  contentHtml?: string | null;
  sourceProvenance?: string | null;
  rightsStatus?: string | null;
  freshnessConfidence?: string | null;
};

export type StrategyGuidePublishResult = {
  published: boolean;
  validation: StrategyGuidePublishValidation;
  guide: StrategyGuideAdminItem | null;
};
