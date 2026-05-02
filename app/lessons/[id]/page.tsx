'use client';

import { useEffect, useRef, useState, type SyntheticEvent } from 'react';
import { ArrowLeft, BookOpen, CheckCircle2, ChevronLeft, ChevronRight, Clock, Download, FileText, LockKeyhole, Video } from 'lucide-react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { fetchVideoLesson, updateVideoProgress } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VideoLessonDetail, VideoLessonProgress } from '@/lib/types/video-lessons';

const CATEGORY_REDIRECTS: Record<string, string> = {
  grammar: '/grammar',
  vocabulary: '/vocabulary',
  strategies: '/strategies',
  pronunciation: '/recalls/words',
  review: '/lessons',
};

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

function progressFromResponse(progress: VideoLessonProgress | null, watchedSeconds: number, completed: boolean, percentComplete: number, lastWatchedAt: string): VideoLessonProgress {
  return {
    watchedSeconds,
    completed,
    percentComplete,
    lastWatchedAt: lastWatchedAt || progress?.lastWatchedAt || null,
  };
}

export default function VideoLessonPage() {
  const params = useParams();
  const router = useRouter();
  const rawId = params?.id;
  const lessonId = Array.isArray(rawId) ? rawId[0] : rawId;
  const [lesson, setLesson] = useState<VideoLessonDetail | null>(null);
  const [transcriptText, setTranscriptText] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const maxWatchedRef = useRef(0);
  const lastReportedRef = useRef(0);
  const resumedRef = useRef(false);

  useEffect(() => {
    if (!lessonId) return;
    const redirect = CATEGORY_REDIRECTS[lessonId.toLowerCase()];
    if (redirect) {
      router.replace(redirect);
      return;
    }

    let cancelled = false;
    resumedRef.current = false;
    maxWatchedRef.current = 0;
    lastReportedRef.current = 0;

    void (async () => {
      await Promise.resolve();
      if (cancelled) return;

      setLoading(true);
      setError(null);
      setTranscriptText(null);
      setLesson(null);

      try {
        const data = await fetchVideoLesson(lessonId);
        if (cancelled) return;
        setLesson(data);
        maxWatchedRef.current = data.progress?.watchedSeconds ?? 0;
        lastReportedRef.current = data.progress?.watchedSeconds ?? 0;
        analytics.track('video_lesson_viewed', { lessonId: data.id, source: data.source });
      } catch {
        if (!cancelled) {
          setLesson(null);
          setError('Could not load video lesson.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [lessonId, router]);

  useEffect(() => {
    if (!lesson?.transcriptUrl || !lesson.isAccessible) return;
    let cancelled = false;

    fetch(lesson.transcriptUrl)
      .then((response) => response.ok ? response.text() : null)
      .then((text) => {
        if (!cancelled && text) setTranscriptText(text);
      })
      .catch(() => {
        if (!cancelled) setTranscriptText(null);
      });

    return () => {
      cancelled = true;
    };
  }, [lesson?.isAccessible, lesson?.transcriptUrl]);

  async function reportProgress(seconds: number) {
    if (!lesson) return;
    const safeSeconds = Math.max(0, Math.floor(seconds));
    if (safeSeconds <= 0 && (lesson.progress?.watchedSeconds ?? 0) <= 0) return;

    try {
      const response = await updateVideoProgress(lesson.id, safeSeconds);
      setLesson((current) => current
        ? {
            ...current,
            progress: progressFromResponse(
              current.progress,
              response.watchedSeconds,
              response.completed,
              response.percentComplete,
              response.lastWatchedAt,
            ),
          }
        : current);
    } catch {
      // Progress is best-effort; playback should never be blocked by a transient sync failure.
    }
  }

  function handleLoadedMetadata(event: SyntheticEvent<HTMLVideoElement>) {
    if (!lesson || resumedRef.current) return;
    const resumeAt = lesson.progress?.completed ? 0 : lesson.progress?.watchedSeconds ?? 0;
    const duration = Number.isFinite(event.currentTarget.duration) ? event.currentTarget.duration : lesson.durationSeconds;
    if (resumeAt > 5 && resumeAt < duration - 10) {
      event.currentTarget.currentTime = resumeAt;
    }
    resumedRef.current = true;
  }

  function handleTimeUpdate(event: SyntheticEvent<HTMLVideoElement>) {
    const watched = Math.floor(event.currentTarget.currentTime);
    maxWatchedRef.current = Math.max(maxWatchedRef.current, watched);
    if (maxWatchedRef.current - lastReportedRef.current >= 15) {
      lastReportedRef.current = maxWatchedRef.current;
      void reportProgress(maxWatchedRef.current);
    }
  }

  function handlePause() {
    if (maxWatchedRef.current > lastReportedRef.current) {
      lastReportedRef.current = maxWatchedRef.current;
      void reportProgress(maxWatchedRef.current);
    }
  }

  function handleEnded() {
    if (!lesson) return;
    maxWatchedRef.current = Math.max(maxWatchedRef.current, lesson.durationSeconds);
    lastReportedRef.current = maxWatchedRef.current;
    void reportProgress(maxWatchedRef.current);
  }

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

  if (!lesson) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Video lesson not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const locked = lesson.requiresUpgrade && !lesson.isAccessible;
  const progress = lesson.progress?.percentComplete ?? 0;

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <div className="flex items-start gap-3">
          <Link href="/lessons" className="mt-1 text-muted/60 hover:text-muted" aria-label="Back to video lessons">
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div className="min-w-0 flex-1">
            <div className="mb-2 flex flex-wrap items-center gap-2 text-xs text-muted">
              <Badge variant="muted">{lesson.examTypeCode.toUpperCase()}</Badge>
              <Badge variant="outline">{SUBTEST_LABELS[lesson.subtestCode ?? ''] ?? 'General'}</Badge>
              <span className="inline-flex items-center gap-1 font-semibold">
                <Clock className="h-3.5 w-3.5" />
                {formatDuration(lesson.durationSeconds)}
              </span>
              {lesson.progress?.completed && <Badge variant="success">Completed</Badge>}
              {locked && <Badge variant="warning">Upgrade required</Badge>}
              {!locked && lesson.isPreviewEligible && <Badge variant="info">Preview access</Badge>}
            </div>
            <h1 className="text-2xl font-bold text-navy">{lesson.title}</h1>
            {lesson.programTitle && (
              <p className="mt-1 text-sm text-muted">
                {lesson.programTitle}{lesson.moduleTitle ? ` / ${lesson.moduleTitle}` : ''}
              </p>
            )}
          </div>
        </div>

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
          <div className="space-y-6">
            <div className="overflow-hidden rounded-2xl bg-black shadow-sm">
              <div className="aspect-video">
                {lesson.videoUrl ? (
                  <video
                    ref={videoRef}
                    src={lesson.videoUrl}
                    controls
                    className="h-full w-full"
                    onLoadedMetadata={handleLoadedMetadata}
                    onTimeUpdate={handleTimeUpdate}
                    onPause={handlePause}
                    onEnded={handleEnded}
                  >
                    {lesson.captionUrl && <track kind="captions" src={lesson.captionUrl} label="English captions" srcLang="en" default />}
                  </video>
                ) : (
                  <div className="flex h-full flex-col items-center justify-center gap-3 px-6 text-center text-white/65">
                    {locked ? <LockKeyhole className="h-16 w-16" /> : <Video className="h-16 w-16" />}
                    <span className="max-w-md text-sm">
                      {locked
                        ? 'This lesson is part of a paid package. Preview or upgrade messaging is handled through your package access.'
                        : 'Video playback is not available for this lesson yet.'}
                    </span>
                  </div>
                )}
              </div>
            </div>

            <Card className="p-5 shadow-sm">
              <div className="flex items-center justify-between gap-4">
                <div>
                  <h2 className="font-semibold text-navy">Progress</h2>
                  <p className="mt-1 text-sm text-muted">
                    Resume at {formatDuration(lesson.progress?.watchedSeconds ?? 0)}.
                  </p>
                </div>
                <span className="text-sm font-bold text-primary">{progress}%</span>
              </div>
              <div className="mt-4 h-2 overflow-hidden rounded-full bg-background-light">
                <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
              </div>
            </Card>

            {lesson.description && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-2 font-semibold text-navy">About this lesson</h2>
                <p className="text-sm leading-6 text-muted">{lesson.description}</p>
              </Card>
            )}

            {(transcriptText || lesson.transcriptUrl) && (
              <Card className="p-5 shadow-sm">
                <div className="mb-4 flex items-center justify-between gap-3">
                  <h2 className="flex items-center gap-2 font-semibold text-navy">
                    <FileText className="h-4 w-4 text-primary" />
                    Transcript
                  </h2>
                  {lesson.transcriptUrl && (
                    <a href={lesson.transcriptUrl} className="text-xs font-semibold text-primary hover:underline" target="_blank" rel="noreferrer">
                      Open transcript
                    </a>
                  )}
                </div>
                {transcriptText ? (
                  <div className="max-h-72 overflow-y-auto whitespace-pre-wrap rounded-lg bg-background-light p-4 text-sm leading-6 text-navy">
                    {transcriptText}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Transcript is available as a downloadable file.</p>
                )}
              </Card>
            )}
          </div>

          <aside className="space-y-4">
            <Card className="p-5 shadow-sm">
              <h2 className="mb-3 flex items-center gap-2 font-semibold text-navy">
                <BookOpen className="h-4 w-4 text-primary" />
                Lesson details
              </h2>
              <div className="space-y-3 text-sm">
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Category</span>
                  <span className="font-semibold text-navy">{labelFor(lesson.category)}</span>
                </div>
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Difficulty</span>
                  <span className="font-semibold text-navy">{labelFor(lesson.difficultyLevel)}</span>
                </div>
                <div className="flex justify-between gap-3">
                  <span className="text-muted">Access</span>
                  <span className="font-semibold text-navy">{labelFor(lesson.accessReason)}</span>
                </div>
              </div>
            </Card>

            {lesson.chapters.length > 0 && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-3 font-semibold text-navy">Chapters</h2>
                <div className="space-y-2">
                  {lesson.chapters.map((chapter) => (
                    <button
                      key={`${chapter.timeSeconds}-${chapter.title}`}
                      type="button"
                      onClick={() => {
                        if (videoRef.current) videoRef.current.currentTime = chapter.timeSeconds;
                      }}
                      className="flex w-full items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-left text-sm hover:border-primary hover:text-primary"
                    >
                      <span>{chapter.title}</span>
                      <span className="font-mono text-xs text-muted">{formatDuration(chapter.timeSeconds)}</span>
                    </button>
                  ))}
                </div>
              </Card>
            )}

            {lesson.resources.length > 0 && (
              <Card className="p-5 shadow-sm">
                <h2 className="mb-3 font-semibold text-navy">Resources</h2>
                <div className="space-y-2">
                  {lesson.resources.map((resource) => (
                    resource.url ? (
                      <a
                        key={`${resource.title}-${resource.url}`}
                        href={resource.url}
                        target="_blank"
                        rel="noreferrer"
                        className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-sm hover:border-primary hover:text-primary"
                      >
                        <span>{resource.title}</span>
                        <Download className="h-4 w-4" />
                      </a>
                    ) : (
                      <div key={resource.title} className="rounded-lg border border-border px-3 py-2 text-sm text-muted">
                        {resource.title}
                      </div>
                    )
                  ))}
                </div>
              </Card>
            )}

            <Card className="p-5 shadow-sm">
              <h2 className="mb-3 font-semibold text-navy">Next step</h2>
              <div className="grid gap-2">
                {lesson.previousLessonId && (
                  <Link href={`/lessons/${lesson.previousLessonId}`} className="inline-flex items-center justify-between rounded-lg border border-border px-3 py-2 text-sm font-semibold text-navy hover:border-primary hover:text-primary">
                    <span className="inline-flex items-center gap-2"><ChevronLeft className="h-4 w-4" /> Previous lesson</span>
                  </Link>
                )}
                {lesson.nextLessonId ? (
                  <Link href={`/lessons/${lesson.nextLessonId}`} className="inline-flex items-center justify-between rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-white hover:bg-primary-dark">
                    <span>Next lesson</span>
                    <ChevronRight className="h-4 w-4" />
                  </Link>
                ) : (
                  <Link href="/lessons" className="inline-flex items-center justify-between rounded-lg bg-primary px-3 py-2 text-sm font-semibold text-white hover:bg-primary-dark">
                    <span>Back to library</span>
                    <ChevronRight className="h-4 w-4" />
                  </Link>
                )}
              </div>
            </Card>

            {lesson.progress?.completed && (
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
