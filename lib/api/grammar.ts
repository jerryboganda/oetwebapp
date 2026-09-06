/**
 * Grammar learner + admin surface — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, asRecord, toNullableString, toStringArray, type ApiRecord } from './client';
import { titleCase, toExamFamilyCode } from '../domain/format';
import type {
  AdminGrammarLessonFull,
  AdminGrammarLessonRow,
  AdminGrammarTopic,
  GrammarAttemptResult,
  GrammarContentBlockLearner,
  GrammarExerciseAuthoring,
  GrammarExerciseLearner,
  GrammarLessonDocument,
  GrammarLessonProgress,
  GrammarLessonSummary,
  GrammarLessonUpsertPayload,
  GrammarRecommendation,
  GrammarTopicLearner,
  GrammarTopicUpsertPayload,
} from '../grammar/types';

const GRAMMAR_PROGRESS_STORAGE_KEY = 'oet-grammar-progress-v1';
const GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY = 'oet-grammar-dismissed-recommendations-v1';
const GRAMMAR_ADMIN_TOPIC_STORAGE_KEY = 'oet-grammar-admin-topics-v1';
const GRAMMAR_ADMIN_LESSON_STORAGE_KEY = 'oet-grammar-admin-lessons-v1';

function readGrammarJsonState<T>(key: string, fallback: T): T {
  if (typeof window === 'undefined') {
    return fallback;
  }

  try {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) as T : fallback;
  } catch {
    return fallback;
  }
}

function writeGrammarJsonState(key: string, value: unknown) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // best-effort cache only
  }
}

function getGrammarProgressCache(): Record<string, GrammarLessonProgress> {
  return readGrammarJsonState<Record<string, GrammarLessonProgress>>(GRAMMAR_PROGRESS_STORAGE_KEY, {});
}

function saveGrammarProgressCache(cache: Record<string, GrammarLessonProgress>) {
  writeGrammarJsonState(GRAMMAR_PROGRESS_STORAGE_KEY, cache);
}

function getDismissedGrammarRecommendations(): Set<string> {
  return new Set(readGrammarJsonState<string[]>(GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY, []));
}

function markGrammarRecommendationDismissed(id: string) {
  const cache = getDismissedGrammarRecommendations();
  cache.add(id);
  writeGrammarJsonState(GRAMMAR_DISMISSED_RECOMMENDATIONS_KEY, Array.from(cache));
}

function getGrammarTopicCache(): Record<string, ApiRecord> {
  return readGrammarJsonState<Record<string, ApiRecord>>(GRAMMAR_ADMIN_TOPIC_STORAGE_KEY, {});
}

function saveGrammarTopicCache(cache: Record<string, ApiRecord>) {
  writeGrammarJsonState(GRAMMAR_ADMIN_TOPIC_STORAGE_KEY, cache);
}

function getGrammarLessonCache(): Record<string, ApiRecord> {
  return readGrammarJsonState<Record<string, ApiRecord>>(GRAMMAR_ADMIN_LESSON_STORAGE_KEY, {});
}

function saveGrammarLessonCache(cache: Record<string, ApiRecord>) {
  writeGrammarJsonState(GRAMMAR_ADMIN_LESSON_STORAGE_KEY, cache);
}

function normalizeGrammarLevel(value: unknown): GrammarLessonSummary['level'] {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'beginner' || normalized === 'intermediate' || normalized === 'advanced') {
    return normalized;
  }

  return 'intermediate';
}

function normalizeGrammarBackendStatus(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'active' || normalized === 'published') return 'published';
  if (normalized === 'draft' || normalized === 'review' || normalized === 'archived') return normalized;
  return normalized || 'draft';
}

function normalizeGrammarRequestStatus(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'published' || normalized === 'active') return 'active';
  if (normalized === 'draft' || normalized === 'review' || normalized === 'archived') return normalized;
  return 'draft';
}

function normalizeGrammarTopicSlug(value: unknown): string {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : 'general';
}

function normalizeGrammarProgressStatus(value: unknown): GrammarLessonProgress['status'] {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  if (normalized === 'not_started' || normalized === 'in_progress' || normalized === 'completed') {
    return normalized;
  }

  return 'not_started';
}

function extractGrammarLessonProgress(value: unknown): GrammarLessonProgress | null {
  const record = asRecord(value);
  if (Object.keys(record).length === 0) {
    return null;
  }

  const score = typeof record.score === 'number'
    ? record.score
    : typeof record.exerciseScore === 'number'
      ? record.exerciseScore
      : null;

  const masteryScore = typeof record.masteryScore === 'number' ? record.masteryScore : score;

  return {
    status: normalizeGrammarProgressStatus(record.status ?? record.state),
    score,
    masteryScore,
    startedAt: toNullableString(record.startedAt),
    completedAt: toNullableString(record.completedAt),
  };
}

function parseGrammarJson(value: unknown): ApiRecord | ApiRecord[] | null {
  if (typeof value !== 'string') return null;

  const trimmed = value.trim();
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) return null;

  try {
    return JSON.parse(trimmed) as ApiRecord | ApiRecord[];
  } catch {
    return null;
  }
}

function toGrammarContentBlocks(value: unknown): GrammarContentBlockLearner[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : `block-${index + 1}`,
      sortOrder: Number(record.sortOrder ?? index + 1) || index + 1,
      type: typeof record.type === 'string' && record.type.length > 0 ? record.type : 'prose',
      contentMarkdown: typeof record.contentMarkdown === 'string'
        ? record.contentMarkdown
        : typeof record.content === 'string'
          ? record.content
          : '',
    };
  });
}

function toGrammarExerciseOptions(value: unknown, type: string): Array<{ id: string; label: string } | { left: string; right: string }> {
  if (!Array.isArray(value)) {
    return [];
  }

  if (type === 'matching') {
    return value.map((item) => {
      const record = asRecord(item);
      return {
        left: typeof record.left === 'string' ? record.left : '',
        right: typeof record.right === 'string' ? record.right : '',
      };
    });
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : String.fromCharCode('a'.charCodeAt(0) + index),
      label: typeof record.label === 'string' ? record.label : typeof record.text === 'string' ? record.text : '',
    };
  });
}

function toGrammarExercises(value: unknown): GrammarExerciseAuthoring[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item, index) => {
    const record = asRecord(item);
    const type = typeof record.type === 'string' && record.type.length > 0 ? record.type : 'fill_blank';
    const options = toGrammarExerciseOptions(record.options, type);
    const correctAnswer = record.correctAnswer ?? (type === 'matching' ? options : '');
    const acceptedAnswers = toStringArray(record.acceptedAnswers ?? record.acceptedAnswersJson ?? []);

    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : `exercise-${index + 1}`,
      sortOrder: Number(record.sortOrder ?? index + 1) || index + 1,
      type: type as GrammarExerciseAuthoring['type'],
      promptMarkdown: typeof record.promptMarkdown === 'string'
        ? record.promptMarkdown
        : typeof record.prompt === 'string'
          ? record.prompt
          : '',
      options,
      correctAnswer,
      acceptedAnswers,
      explanationMarkdown: typeof record.explanationMarkdown === 'string'
        ? record.explanationMarkdown
        : typeof record.explanation === 'string'
          ? record.explanation
          : '',
      difficulty: normalizeGrammarLevel(record.difficulty),
      points: Number(record.points ?? 1) || 1,
    };
  });
}

function parseGrammarLessonDocument(content: string | null | undefined, exercisesJson: string | null | undefined): GrammarLessonDocument {
  const parsedContent = parseGrammarJson(content);
  const contentRecord = parsedContent && !Array.isArray(parsedContent) ? parsedContent : null;

  const topicId = typeof contentRecord?.topicId === 'string' && contentRecord.topicId.trim().length > 0
    ? contentRecord.topicId.trim()
    : typeof contentRecord?.topicSlug === 'string' && contentRecord.topicSlug.trim().length > 0
      ? contentRecord.topicSlug.trim()
      : null;

  const category = normalizeGrammarTopicSlug(
    typeof contentRecord?.category === 'string' && contentRecord.category.trim().length > 0
      ? contentRecord.category
      : topicId ?? contentRecord?.slug,
  );

  const rawExercises = contentRecord?.exercises ?? parseGrammarJson(exercisesJson) ?? [];
  const contentBlocks = toGrammarContentBlocks(contentRecord?.contentBlocks ?? contentRecord?.blocks ?? []);

  const fallbackText = typeof content === 'string' && content.trim().length > 0 ? content.trim() : '';
  const blocks = contentBlocks.length > 0
    ? contentBlocks
    : fallbackText
      ? [{ id: 'content', sortOrder: 1, type: 'prose', contentMarkdown: fallbackText }]
      : [];

  return {
    topicId,
    category,
    sourceProvenance: typeof contentRecord?.sourceProvenance === 'string' ? contentRecord.sourceProvenance : '',
    prerequisiteLessonIds: toStringArray(contentRecord?.prerequisiteLessonIds ?? contentRecord?.prerequisiteLessonId ?? []),
    contentBlocks: blocks,
    exercises: toGrammarExercises(rawExercises),
    version: Number(contentRecord?.version ?? 1) || 1,
    updatedAt: typeof contentRecord?.updatedAt === 'string' ? contentRecord.updatedAt : new Date().toISOString(),
  };
}

function stripGrammarExerciseAnswers(exercise: GrammarExerciseAuthoring): GrammarExerciseLearner {
  const { correctAnswer, acceptedAnswers, explanationMarkdown, id, options, type, difficulty, ...rest } = exercise;
  void correctAnswer;
  void acceptedAnswers;
  void explanationMarkdown;
  return {
    ...rest,
    id: id ?? `exercise-${exercise.sortOrder}`,
    type: type as GrammarExerciseLearner['type'],
    options: Array.isArray(options)
      ? (options as GrammarExerciseLearner['options'])
      : [],
    difficulty: difficulty as GrammarExerciseLearner['difficulty'],
  };
}

function toGrammarLessonProgressFromAttempt(attempt: GrammarAttemptResult): GrammarLessonProgress {
  if (attempt.progress) {
    return attempt.progress;
  }

  return {
    status: 'completed',
    score: attempt.score,
    masteryScore: attempt.masteryScore,
    startedAt: null,
    completedAt: new Date().toISOString(),
  };
}

function buildGrammarLessonSummary(raw: ApiRecord, progress?: GrammarLessonProgress | null): GrammarLessonSummary {
  const category = normalizeGrammarTopicSlug(raw.category ?? raw.topicId ?? raw.topicSlug ?? 'general');
  const currentProgress = progress ?? null;
  const masteryScore = currentProgress?.masteryScore ?? currentProgress?.score ?? 0;

  return {
    id: String(raw.id ?? raw.lessonId ?? ''),
    examTypeCode: toExamFamilyCode(raw.examTypeCode ?? raw.profession ?? 'oet'),
    topicId: typeof raw.topicId === 'string' && raw.topicId.length > 0 ? raw.topicId : category,
    topicSlug: category,
    topicName: titleCase(category),
    category,
    title: typeof raw.title === 'string' ? raw.title : '',
    description: toNullableString(raw.description),
    level: normalizeGrammarLevel(raw.level),
    estimatedMinutes: Number(raw.estimatedMinutes ?? raw.estimatedDurationMinutes ?? 0) || 0,
    sortOrder: Number(raw.sortOrder ?? 0) || 0,
    exerciseCount: Number(raw.exerciseCount ?? 0) || 0,
    progress: currentProgress,
    mastered: currentProgress?.status === 'completed' && masteryScore >= 80,
    statusLabel: currentProgress?.status === 'completed'
      ? 'Completed'
      : currentProgress?.status === 'in_progress'
        ? 'In progress'
        : 'New',
  };
}

function deriveGrammarOverviewLessons(rawLessons: ApiRecord[], progressCache: Record<string, GrammarLessonProgress>) {
  return rawLessons.map((lesson) => {
    const lessonId = String(lesson.id ?? lesson.lessonId ?? '');
    const serverProgress = extractGrammarLessonProgress(lesson.progress);
    return buildGrammarLessonSummary(lesson, serverProgress ?? progressCache[lessonId] ?? null);
  });
}

function buildGrammarTopicsFromLessons(lessons: GrammarLessonSummary[], fallbackExamTypeCode: string, topicCache: Record<string, ApiRecord>): GrammarTopicLearner[] {
  const grouped = new Map<string, GrammarLessonSummary[]>();
  lessons.forEach((lesson) => {
    const slug = normalizeGrammarTopicSlug(lesson.topicSlug ?? lesson.category ?? lesson.topicId ?? 'general');
    const list = grouped.get(slug) ?? [];
    list.push(lesson);
    grouped.set(slug, list);
  });

  return Array.from(grouped.entries())
    .map(([slug, list]) => {
      const cached = topicCache[slug] ?? {};
      const topicLessons = list.sort((a, b) => a.sortOrder - b.sortOrder);
      const lessonCount = topicLessons.length;
      const masteredLessonCount = topicLessons.filter((lesson) => lesson.mastered).length;
      const completedLessonCount = topicLessons.filter((lesson) => lesson.progress?.status === 'completed').length;

      return {
        id: typeof cached.id === 'string' && cached.id.length > 0 ? cached.id : slug,
        slug,
        name: typeof cached.name === 'string' && cached.name.length > 0 ? cached.name : titleCase(slug),
        description: toNullableString(cached.description) ?? `Strengthen ${titleCase(slug).toLowerCase()} patterns.`,
        iconEmoji: toNullableString(cached.iconEmoji) ?? '📘',
        levelHint: typeof cached.levelHint === 'string' && cached.levelHint.length > 0 ? cached.levelHint : fallbackExamTypeCode.toUpperCase(),
        lessonCount,
        masteredLessonCount,
        completedLessonCount,
      };
    })
    .sort((a, b) => a.lessonCount - b.lessonCount ? b.lessonCount - a.lessonCount : a.name.localeCompare(b.name));
}

function buildGrammarRecommendations(lessons: GrammarLessonSummary[], dismissed: Set<string>): GrammarRecommendation[] {
  return lessons
    .filter((lesson) => !dismissed.has(lesson.id))
    .filter((lesson) => lesson.progress?.status !== 'completed')
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder)
    .slice(0, 3)
    .map((lesson) => ({
      id: `rec-${lesson.id}`,
      lessonId: lesson.id,
      title: lesson.title,
      reason: lesson.progress?.status === 'in_progress'
        ? `Finish your current ${titleCase(lesson.category).toLowerCase()} lesson.`
        : `Build confidence with ${titleCase(lesson.category).toLowerCase()} patterns.`,
      topicSlug: lesson.topicSlug,
      topicName: lesson.topicName,
      level: lesson.level,
      estimatedMinutes: lesson.estimatedMinutes,
      actionLabel: 'Open lesson',
    }));
}

async function fetchGrammarLessonsRaw(params?: { examTypeCode?: string; category?: string; level?: string }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.category) p.set('category', params.category);
  if (params?.level) p.set('level', params.level);
  return apiRequest<ApiRecord[]>(`/v1/grammar/lessons?${p}`);
}

export async function fetchGrammarLessons(params?: { examTypeCode?: string; category?: string; level?: string }) {
  const rawLessons = await fetchGrammarLessonsRaw(params);
  const progressCache = getGrammarProgressCache();

  const lessons = deriveGrammarOverviewLessons(rawLessons, progressCache);

  return Promise.all(
    lessons.map(async (lesson) => {
      try {
        const detail = await fetchGrammarLesson(lesson.id);
        return {
          ...lesson,
          exerciseCount: detail.exercises.length,
          progress: detail.progress ?? lesson.progress ?? null,
          mastered: detail.mastered ?? lesson.mastered,
        };
      } catch {
        return lesson;
      }
    }),
  );
}

export async function fetchGrammarLesson(lessonId: string) {
  const raw = await apiRequest<ApiRecord>(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}`);
  const document = parseGrammarLessonDocument(raw.contentHtml ?? raw.content ?? '', raw.exercisesJson ?? '[]');
  const progress = extractGrammarLessonProgress(raw.progress) ?? getGrammarProgressCache()[lessonId] ?? null;

  const summary = buildGrammarLessonSummary(raw, progress);
  const result = {
    ...summary,
    contentBlocks: document.contentBlocks,
    exercises: document.exercises.map(stripGrammarExerciseAnswers),
    sourceProvenance: document.sourceProvenance || null,
  };

  return result;
}

export async function startGrammarLesson(lessonId: string) {
  const result = await apiRequest(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/start`, { method: 'POST' });
  const progressCache = getGrammarProgressCache();
  progressCache[lessonId] = {
    status: 'in_progress',
    score: null,
    masteryScore: null,
    startedAt: new Date().toISOString(),
    completedAt: null,
  };
  saveGrammarProgressCache(progressCache);
  return result;
}

export async function submitGrammarAttempt(lessonId: string, answers: Record<string, unknown>) {
  const result = await apiRequest<GrammarAttemptResult>(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/submit`, {
    method: 'POST',
    body: JSON.stringify({ answersJson: JSON.stringify(answers) }),
  });

  const progressCache = getGrammarProgressCache();
  progressCache[lessonId] = toGrammarLessonProgressFromAttempt(result);
  saveGrammarProgressCache(progressCache);

  return result;
}

export async function completeGrammarLesson(lessonId: string, score: number, answersJson: string) {
  return apiRequest(`/v1/grammar/lessons/${encodeURIComponent(lessonId)}/complete`, {
    method: 'POST',
    body: JSON.stringify({ score, answersJson }),
  });
}

export async function fetchGrammarOverview(examTypeCode = 'oet') {
  const rawLessons = await fetchGrammarLessonsRaw({ examTypeCode });
  const progressCache = getGrammarProgressCache();
  const lessons = deriveGrammarOverviewLessons(rawLessons, progressCache);
  const topicCache = getGrammarTopicCache();
  const dismissed = getDismissedGrammarRecommendations();

  const topics = buildGrammarTopicsFromLessons(lessons, examTypeCode, topicCache);
  const completedLessons = lessons.filter((lesson) => lesson.progress?.status === 'completed');
  const masteredLessons = completedLessons.filter((lesson) => (lesson.progress?.masteryScore ?? lesson.progress?.score ?? 0) >= 80);
  const scores = completedLessons
    .map((lesson) => lesson.progress?.masteryScore ?? lesson.progress?.score ?? 0)
    .filter((score) => typeof score === 'number' && Number.isFinite(score));

  return {
    examTypeCode,
    topics,
    lessonsMastered: masteredLessons.length,
    lessonsCompleted: completedLessons.length,
    lessonsTotal: lessons.length,
    overallMasteryScore: scores.length > 0 ? Math.round(scores.reduce((sum, score) => sum + score, 0) / scores.length) : 0,
    recommendations: buildGrammarRecommendations(lessons, dismissed),
  };
}

export async function fetchGrammarTopicDetail(slug: string) {
  const rawLessons = await fetchGrammarLessonsRaw({ category: slug });
  const progressCache = getGrammarProgressCache();
  const lessons = await fetchGrammarLessons({ category: slug });
  const baseLessons = deriveGrammarOverviewLessons(rawLessons, progressCache);
  const topicCache = getGrammarTopicCache();
  const topicMeta = topicCache[slug] ?? {};
  const sourceLessons = lessons.length > 0 ? lessons : baseLessons;

  return {
    topic: {
      id: typeof topicMeta.id === 'string' && topicMeta.id.length > 0 ? topicMeta.id : slug,
      slug,
      name: typeof topicMeta.name === 'string' && topicMeta.name.length > 0 ? topicMeta.name : titleCase(slug),
      description: toNullableString(topicMeta.description) ?? null,
      iconEmoji: toNullableString(topicMeta.iconEmoji) ?? null,
      levelHint: typeof topicMeta.levelHint === 'string' && topicMeta.levelHint.length > 0 ? topicMeta.levelHint : 'All levels',
    },
    lessons: sourceLessons,
  };
}

export async function dismissGrammarRecommendation(id: string) {
  markGrammarRecommendationDismissed(id);
  return { dismissed: true };
}

function createGrammarLessonPayload(payload: GrammarLessonUpsertPayload) {
  return {
    topicId: payload.topicId,
    category: payload.category,
    sourceProvenance: payload.sourceProvenance,
    prerequisiteLessonIds: payload.prerequisiteLessonIds,
    contentBlocks: payload.contentBlocks,
    exercises: payload.exercises,
    version: 1,
    updatedAt: new Date().toISOString(),
  };
}

function cacheAdminGrammarLesson(lessonId: string, data: ApiRecord) {
  const cache = getGrammarLessonCache();
  cache[lessonId] = data;
  saveGrammarLessonCache(cache);
}

function cacheAdminGrammarTopic(topic: AdminGrammarTopic) {
  const cache = getGrammarTopicCache();
  cache[topic.slug] = topic as ApiRecord;
  saveGrammarTopicCache(cache);
}

function buildGrammarTopicFromLessons(slug: string, lessons: GrammarLessonSummary[], examTypeCode: string): AdminGrammarTopic {
  const topicCache = getGrammarTopicCache();
  const cached = topicCache[slug] ?? {};
  return {
    id: typeof cached.id === 'string' && cached.id.length > 0 ? cached.id : slug,
    slug,
    name: typeof cached.name === 'string' && cached.name.length > 0 ? cached.name : titleCase(slug),
    description: toNullableString(cached.description) ?? null,
    iconEmoji: toNullableString(cached.iconEmoji) ?? '📘',
    levelHint: typeof cached.levelHint === 'string' && cached.levelHint.length > 0 ? cached.levelHint : 'All levels',
    lessonCount: lessons.length,
    masteredLessonCount: lessons.filter((lesson) => lesson.mastered).length,
    completedLessonCount: lessons.filter((lesson) => lesson.progress?.status === 'completed').length,
    examTypeCode,
    status: normalizeGrammarBackendStatus(cached.status ?? 'published'),
    sortOrder: Number(cached.sortOrder ?? 0) || 0,
    createdAt: typeof cached.createdAt === 'string' ? cached.createdAt : null,
    updatedAt: typeof cached.updatedAt === 'string' ? cached.updatedAt : null,
  };
}

export async function adminListGrammarTopics(params?: { examTypeCode?: string }) {
  const lessons = await adminListGrammarLessonsV2({ examTypeCode: params?.examTypeCode ?? 'oet', pageSize: 500 });
  const topicMap = new Map<string, GrammarLessonSummary[]>();

  lessons.items.forEach((lesson) => {
    const slug = normalizeGrammarTopicSlug(lesson.topicSlug ?? lesson.category ?? lesson.topicId ?? 'general');
    const list = topicMap.get(slug) ?? [];
    list.push(lesson);
    topicMap.set(slug, list);
  });

  const topics = Array.from(topicMap.entries()).map(([slug, groupedLessons]) => buildGrammarTopicFromLessons(slug, groupedLessons, params?.examTypeCode ?? 'oet'));
  const cachedTopics = Object.values(getGrammarTopicCache()).map((topic) => {
    const record = topic as ApiRecord;
    return {
      id: typeof record.id === 'string' && record.id.length > 0 ? record.id : normalizeGrammarTopicSlug(record.slug ?? record.name),
      slug: normalizeGrammarTopicSlug(record.slug ?? record.id ?? record.name),
      name: typeof record.name === 'string' && record.name.length > 0 ? record.name : titleCase(normalizeGrammarTopicSlug(record.slug ?? record.id ?? record.name)),
      description: toNullableString(record.description) ?? null,
      iconEmoji: toNullableString(record.iconEmoji) ?? '📘',
      levelHint: typeof record.levelHint === 'string' && record.levelHint.length > 0 ? record.levelHint : 'All levels',
      lessonCount: Number(record.lessonCount ?? 0) || 0,
      masteredLessonCount: Number(record.masteredLessonCount ?? 0) || 0,
      completedLessonCount: Number(record.completedLessonCount ?? 0) || 0,
      examTypeCode: typeof record.examTypeCode === 'string' && record.examTypeCode.length > 0 ? record.examTypeCode : params?.examTypeCode ?? 'oet',
      status: normalizeGrammarBackendStatus(record.status ?? 'published'),
      sortOrder: Number(record.sortOrder ?? 0) || 0,
      createdAt: typeof record.createdAt === 'string' ? record.createdAt : null,
      updatedAt: typeof record.updatedAt === 'string' ? record.updatedAt : null,
    } as AdminGrammarTopic;
  });

  const merged = new Map<string, AdminGrammarTopic>();
  [...topics, ...cachedTopics].forEach((topic) => {
    merged.set(topic.slug, topic);
  });

  return Array.from(merged.values()).filter((topic) => !params?.examTypeCode || topic.examTypeCode === params.examTypeCode);
}

export async function adminCreateGrammarTopic(payload: GrammarTopicUpsertPayload) {
  const topic: AdminGrammarTopic = {
    id: payload.slug,
    slug: payload.slug,
    name: payload.name,
    description: payload.description,
    iconEmoji: payload.iconEmoji ?? '📘',
    levelHint: payload.levelHint,
    lessonCount: 0,
    masteredLessonCount: 0,
    completedLessonCount: 0,
    examTypeCode: payload.examTypeCode,
    status: normalizeGrammarBackendStatus(payload.status ?? 'published'),
    sortOrder: payload.sortOrder,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
  cacheAdminGrammarTopic(topic);
  return topic;
}

export async function adminUpdateGrammarTopic(topicId: string, patch: Partial<GrammarTopicUpsertPayload & { status: string }>) {
  const cache = getGrammarTopicCache();
  const existing = cache[topicId] ?? cache[normalizeGrammarTopicSlug(topicId)] ?? {};
  const slug = normalizeGrammarTopicSlug(existing.slug ?? topicId);
  const updated: AdminGrammarTopic = {
    id: typeof existing.id === 'string' && existing.id.length > 0 ? existing.id : slug,
    slug,
    name: typeof patch.name === 'string' ? patch.name : typeof existing.name === 'string' ? existing.name : titleCase(slug),
    description: patch.description === undefined ? toNullableString(existing.description) ?? null : patch.description,
    iconEmoji: patch.iconEmoji === undefined ? toNullableString(existing.iconEmoji) ?? '📘' : patch.iconEmoji,
    levelHint: typeof patch.levelHint === 'string' ? patch.levelHint : typeof existing.levelHint === 'string' ? existing.levelHint : 'All levels',
    lessonCount: Number(existing.lessonCount ?? 0) || 0,
    masteredLessonCount: Number(existing.masteredLessonCount ?? 0) || 0,
    completedLessonCount: Number(existing.completedLessonCount ?? 0) || 0,
    examTypeCode: typeof patch.examTypeCode === 'string' ? patch.examTypeCode : typeof existing.examTypeCode === 'string' ? existing.examTypeCode : 'oet',
    status: normalizeGrammarBackendStatus(patch.status ?? existing.status ?? 'published'),
    sortOrder: typeof patch.sortOrder === 'number' ? patch.sortOrder : Number(existing.sortOrder ?? 0) || 0,
    createdAt: typeof existing.createdAt === 'string' ? existing.createdAt : new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
  cache[slug] = updated as ApiRecord;
  saveGrammarTopicCache(cache);
  return updated;
}

export async function adminArchiveGrammarTopic(topicId: string) {
  return adminUpdateGrammarTopic(topicId, { status: 'archived' });
}

export async function adminListGrammarLessonsV2(params?: { examTypeCode?: string; status?: string; topicId?: string; search?: string; page?: number; pageSize?: number }) {
  const query = new URLSearchParams();
  if (params?.examTypeCode) query.set('profession', params.examTypeCode);
  if (params?.status) query.set('status', normalizeGrammarRequestStatus(params.status));
  if (params?.search) query.set('search', params.search);
  query.set('page', String(params?.page ?? 1));
  query.set('pageSize', String(params?.pageSize ?? 20));

  const response = await apiRequest<{ total: number; page: number; pageSize: number; items: ApiRecord[] }>(`/v1/admin/grammar/lessons?${query}`);
  const cache = getGrammarLessonCache();

  const items: AdminGrammarLessonRow[] = (response.items ?? []).map((item) => {
    const category = normalizeGrammarTopicSlug(item.category ?? item.Category ?? 'general');
    const id = String(item.id ?? item.Id ?? '');
    const cached = cache[id] ?? {};
    const updatedAt = typeof cached.updatedAt === 'string'
      ? cached.updatedAt
      : typeof cached.updated_at === 'string'
        ? cached.updated_at
        : new Date().toISOString();
    const publishState = normalizeGrammarBackendStatus(item.status ?? 'draft');
    const cachedExercises = Array.isArray(cached.exercises) ? cached.exercises : [];
    const progress = getGrammarProgressCache()[id] ?? null;
    const masteryScore = progress?.masteryScore ?? progress?.score ?? 0;

    return {
      id,
      examTypeCode: toExamFamilyCode(item.profession ?? item.examTypeCode ?? 'oet'),
      topicId: typeof item.topicId === 'string' && item.topicId.length > 0 ? String(item.topicId) : category,
      topicSlug: category,
      topicName: titleCase(category),
      category,
      title: typeof item.title === 'string' ? item.title : '',
      description: toNullableString(item.description) ?? toNullableString(cached.description),
      level: normalizeGrammarLevel(item.difficulty ?? item.level),
      estimatedMinutes: Number(item.estimatedDurationMinutes ?? item.estimatedMinutes ?? 0) || 0,
      sortOrder: Number(item.sortOrder ?? cached.sortOrder ?? 0) || 0,
      exerciseCount: Number(item.exerciseCount ?? cachedExercises.length ?? 0) || 0,
      progress,
      mastered: progress?.status === 'completed' && masteryScore >= 80,
      statusLabel: publishState,
      status: publishState,
      publishState,
      updatedAt,
    };
  });

  const filtered = params?.topicId
    ? items.filter((item) => item.topicId === params.topicId || item.category === params.topicId)
    : items;

  return {
    total: params?.topicId ? filtered.length : response.total,
    page: response.page,
    pageSize: response.pageSize,
    items: filtered,
  };
}

export async function adminGetGrammarLessonV2(lessonId: string) {
  const raw = await apiRequest<ApiRecord>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}`);
  const document = parseGrammarLessonDocument(raw.content ?? raw.contentHtml ?? '', raw.exercisesJson ?? '[]');
  const publishState = normalizeGrammarBackendStatus(raw.status ?? raw.Status ?? 'draft');
  const lesson: AdminGrammarLessonFull = {
    id: String(raw.id ?? raw.Id ?? lessonId),
    examTypeCode: toExamFamilyCode(raw.profession ?? raw.examTypeCode ?? 'oet'),
    topicId: document.topicId ?? document.category,
    topicSlug: document.category,
    topicName: titleCase(document.category),
    category: document.category,
    title: typeof raw.title === 'string' ? raw.title : '',
    description: toNullableString(raw.description),
    level: normalizeGrammarLevel(raw.difficulty ?? raw.level),
    estimatedMinutes: Number(raw.estimatedDurationMinutes ?? raw.estimatedMinutes ?? 0) || 0,
    sortOrder: Number(raw.sortOrder ?? 0) || 0,
    exerciseCount: document.exercises.length,
    progress: getGrammarProgressCache()[lessonId] ?? null,
    mastered: false,
    statusLabel: publishState,
    contentBlocks: document.contentBlocks,
    exercises: document.exercises,
    publishState,
    version: document.version,
    sourceProvenance: document.sourceProvenance || '',
    prerequisiteLessonIds: document.prerequisiteLessonIds,
    status: normalizeGrammarBackendStatus(raw.status ?? 'draft'),
    createdAt: typeof raw.createdAt === 'string' ? raw.createdAt : null,
    updatedAt: document.updatedAt,
    publishedAt: typeof raw.publishedAt === 'string' ? raw.publishedAt : null,
  };
  cacheAdminGrammarLesson(lesson.id, { ...lesson, updatedAt: lesson.updatedAt });
  return lesson;
}

export async function adminCreateGrammarLessonV2(payload: GrammarLessonUpsertPayload) {
  const category = normalizeGrammarTopicSlug(payload.category || payload.topicId || 'general');
  const document = createGrammarLessonPayload(payload);
  const response = await apiRequest<ApiRecord>('/v1/admin/grammar/lessons', {
    method: 'POST',
    body: JSON.stringify({
      title: payload.title,
      professionId: payload.examTypeCode,
      category,
      description: payload.description,
      content: JSON.stringify(document),
      difficulty: payload.level,
      estimatedDurationMinutes: payload.estimatedMinutes,
      sortOrder: payload.sortOrder,
    }),
  });

  const lessonId = String(response.id ?? response.Id ?? `lesson-${Date.now()}`);
  cacheAdminGrammarLesson(lessonId, { ...document, id: lessonId, title: payload.title, description: payload.description, examTypeCode: payload.examTypeCode, category, status: 'draft', publishState: 'draft', updatedAt: document.updatedAt });
  return { id: lessonId, status: normalizeGrammarBackendStatus(response.status ?? 'draft') };
}

export async function adminUpdateGrammarLessonV2(lessonId: string, payload: GrammarLessonUpsertPayload & { status?: string }) {
  const existing = getGrammarLessonCache()[lessonId] ?? {};
  const category = normalizeGrammarTopicSlug(payload.category || payload.topicId || existing.category || 'general');
  const document = {
    ...createGrammarLessonPayload(payload),
    version: Number(existing.version ?? 0) + 1 || 1,
  };

  const response = await apiRequest<ApiRecord>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}`, {
    method: 'PUT',
    body: JSON.stringify({
      title: payload.title,
      professionId: payload.examTypeCode,
      category,
      description: payload.description,
      content: JSON.stringify(document),
      difficulty: payload.level,
      estimatedDurationMinutes: payload.estimatedMinutes,
      sortOrder: payload.sortOrder,
      status: payload.status,
    }),
  });
  cacheAdminGrammarLesson(lessonId, { ...document, id: lessonId, title: payload.title, description: payload.description, examTypeCode: payload.examTypeCode, category, status: normalizeGrammarBackendStatus(payload.status ?? response.status ?? 'draft'), publishState: normalizeGrammarBackendStatus(payload.status ?? response.status ?? 'draft'), updatedAt: document.updatedAt });
  return response;
}

export async function adminPublishGrammarLesson(lessonId: string) {
  return adminUpdateGrammarLessonV2(lessonId, { ...(getGrammarLessonCache()[lessonId] ?? {}), status: 'active' } as GrammarLessonUpsertPayload & { status?: string });
}

export async function adminUnpublishGrammarLesson(lessonId: string) {
  return adminUpdateGrammarLessonV2(lessonId, { ...(getGrammarLessonCache()[lessonId] ?? {}), status: 'draft' } as GrammarLessonUpsertPayload & { status?: string });
}

export async function adminArchiveGrammarLessonV2(lessonId: string) {
  const response = await apiRequest(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/archive`, { method: 'POST' });
  const cache = getGrammarLessonCache();
  if (cache[lessonId]) {
    cache[lessonId] = { ...cache[lessonId], status: 'archived', publishState: 'archived', updatedAt: new Date().toISOString() };
    saveGrammarLessonCache(cache);
  }
  return response;
}

/** Permanently deletes an archived grammar lesson + all learner progress. system_admin only. */
export async function adminForceDeleteGrammarLessonV2(lessonId: string) {
  const response = await apiRequest(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/force-delete`, { method: 'POST' });
  const cache = getGrammarLessonCache();
  if (cache[lessonId]) {
    delete cache[lessonId];
    saveGrammarLessonCache(cache);
  }
  return response;
}

export async function adminEvaluateGrammarPublishGate(lessonId: string) {
  const lesson = await adminGetGrammarLessonV2(lessonId);
  const errors: string[] = [];

  if (!lesson.title.trim()) errors.push('Title is required.');
  if (!lesson.description?.trim()) errors.push('Description is required.');
  if (!lesson.sourceProvenance?.trim()) errors.push('Source provenance is required.');
  if (!lesson.category?.trim()) errors.push('Category is required.');
  if (!lesson.contentBlocks.length) errors.push('Add at least one content block.');
  if (!lesson.exercises.length) errors.push('Add at least one exercise.');

  lesson.exercises.forEach((exercise, index) => {
    if (!exercise.promptMarkdown.trim()) errors.push(`Exercise ${index + 1} needs a prompt.`);
    if (exercise.type === 'matching' && (!Array.isArray(exercise.options) || exercise.options.length === 0)) errors.push(`Exercise ${index + 1} needs matching pairs.`);
    if (exercise.type !== 'matching' && exercise.correctAnswer == null) errors.push(`Exercise ${index + 1} needs a correct answer.`);
    if (!exercise.explanationMarkdown.trim()) errors.push(`Exercise ${index + 1} needs an explanation.`);
  });

  return {
    canPublish: errors.length === 0,
    errors,
  };
}

export async function adminGenerateGrammarAiDraft(params: { examTypeCode: string; topicSlug?: string; prompt: string; level: string; targetExerciseCount: number; profession?: string; }) {
  // Grounded, platform-only. The backend builds the AI prompt via
  // IAiGatewayService and refuses ungrounded prompts. The client must not
  // create local lesson content when the backend is unavailable.
  return apiRequest<{
    lessonId: string;
    title: string;
    contentBlockCount: number;
    exerciseCount: number;
    rulebookVersion: string;
    appliedRuleIds: string[];
    warning: string | null;
  }>('/v1/admin/grammar/ai-draft', {
    method: 'POST',
    body: JSON.stringify({
      examTypeCode: params.examTypeCode,
      topicSlug: params.topicSlug ?? null,
      prompt: params.prompt,
      level: params.level,
      targetExerciseCount: params.targetExerciseCount,
      profession: params.profession ?? 'medicine',
    }),
  });
}

export interface GrammarEntitlement {
  allowed: boolean;
  tier: string;
  remaining: number | null;
  limitPerWindow: number | null;
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

export async function adminGenerateWritingAiDraft(params: {
  prompt: string;
  profession: string;
  letterType: string;
  recipientSpecialty?: string;
  difficulty: string;
  targetCaseNoteCount: number;
}) {
  // Grounded, platform-only. The backend builds the AI prompt via
  // IAiGatewayService and refuses ungrounded prompts. On AI-parse failure
  // the server returns a deterministic starter template + `warning`.
  return apiRequest<{
    contentId: string;
    title: string;
    caseNoteCount: number;
    modelLetterWordCount: number;
    rulebookVersion: string;
    appliedRuleIds: string[];
    warning: string | null;
  }>('/v1/admin/writing/ai-draft', {
    method: 'POST',
    body: JSON.stringify({
      prompt: params.prompt,
      profession: params.profession,
      letterType: params.letterType,
      recipientSpecialty: params.recipientSpecialty ?? null,
      difficulty: params.difficulty,
      targetCaseNoteCount: params.targetCaseNoteCount,
    }),
  });
}

export async function fetchGrammarEntitlement(): Promise<GrammarEntitlement> {
  return apiRequest<GrammarEntitlement>('/v1/grammar/entitlement');
}

export async function adminFetchGrammarPublishGate(lessonId: string) {
  return apiRequest<{ canPublish: boolean; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish-gate`);
}

export async function adminPublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ published: boolean; status: string; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish`, { method: 'POST' });
}

export async function adminUnpublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ id: string; status: string }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/unpublish`, { method: 'POST' });
}

export async function adminFetchGrammarStats(lessonId: string) {
  return apiRequest<{
    lessonId: string;
    attempts: number;
    uniqueLearners: number;
    averageMasteryScore: number;
    reviewItemsCreated: number;
  }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/stats`);
}
