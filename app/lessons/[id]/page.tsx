'use client';

import { useEffect, useState, useRef } from 'react';
import { ArrowLeft, Video, Clock } from 'lucide-react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchVideoLesson, updateVideoProgress } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type VideoLesson = { id: string; examTypeCode: string; subtestCode: string | null; title: string; description: string | null; durationSeconds: number; videoUrl: string | null; transcriptJson: string | null; difficultyLevel: string };

/* Slug-based lesson category routes that should redirect to their own pages */
const CATEGORY_REDIRECTS: Record<string, string> = {
  grammar: '/grammar',
  vocabulary: '/vocabulary',
  strategies: '/strategies',
  pronunciation: '/pronunciation',
  review: '/lessons',
};

function formatDuration(seconds: number) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export default function VideoLessonPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const [lesson, setLesson] = useState<VideoLesson | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const progressRef = useRef(0);
  const reportedRef = useRef(false);

  useEffect(() => {
    if (!params.id) return;
    /* Redirect category slugs to their dedicated pages */
    const redirect = CATEGORY_REDIRECTS[params.id.toLowerCase()];
    if (redirect) {
      router.replace(redirect);
      return;
    }
    fetchVideoLesson(params.id).then(data => {
      setLesson(data as VideoLesson);
      setLoading(false);
      analytics.track('video_lesson_viewed', { lessonId: params.id });
    }).catch(() => {
      setError('Could not load video lesson.');
      setLoading(false);
    });
  }, [params.id, router]);

  function handleTimeUpdate(e: React.SyntheticEvent<HTMLVideoElement>) {
    const video = e.currentTarget;
    const watched = Math.floor(video.currentTime);
    progressRef.current = watched;
    // Report progress every 30 seconds
    if (watched > 0 && watched % 30 === 0 && !reportedRef.current) {
      reportedRef.current = true;
      updateVideoProgress(params.id!, watched).catch(() => {});
      setTimeout(() => { reportedRef.current = false; }, 5000);
    }
  }

  function handleEnded() {
    if (lesson) updateVideoProgress(lesson.id, lesson.durationSeconds).catch(() => {});
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-8 w-48 mb-4" />
        <Skeleton className="aspect-video rounded-2xl mb-4" />
      </LearnerDashboardShell>
    );
  }

  if (!lesson) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">Video lesson not found.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/lessons" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div>
          <div className="flex items-center gap-2 text-xs text-gray-400 mb-0.5">
            <Video className="w-3.5 h-3.5 text-blue-400" />
            <span className="uppercase font-medium">{lesson.examTypeCode}</span>
            {lesson.subtestCode && <span className="capitalize">· {lesson.subtestCode}</span>}
            <span className="flex items-center gap-1"><Clock className="w-3 h-3" />{formatDuration(lesson.durationSeconds)}</span>
          </div>
          <h1 className="text-xl font-bold text-gray-900 dark:text-white">{lesson.title}</h1>
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="max-w-3xl mx-auto">
        {/* Video player */}
        <div className="bg-black rounded-2xl overflow-hidden mb-6 aspect-video flex items-center justify-center">
          {lesson.videoUrl ? (
            <video
              src={lesson.videoUrl}
              controls
              className="w-full h-full"
              onTimeUpdate={handleTimeUpdate}
              onEnded={handleEnded}
            />
            ) : (
              <div className="flex flex-col items-center gap-3 text-white/50">
                <Video className="w-16 h-16" />
                <span className="text-sm">Video is not available for this lesson yet.</span>
              </div>
            )}
        </div>

        {/* Description */}
        {lesson.description && (
          <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5 mb-6">
            <h2 className="font-semibold text-gray-900 dark:text-white mb-2">About this lesson</h2>
            <p className="text-sm text-gray-600 dark:text-gray-400">{lesson.description}</p>
          </div>
        )}

        {/* Transcript */}
        {lesson.transcriptJson && (() => {
          try {
            const t = JSON.parse(lesson.transcriptJson) as { segments?: Array<{ time: number; text: string }> };
            if (!t.segments?.length) return null;
            return (
              <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-5">
                <h2 className="font-semibold text-gray-900 dark:text-white mb-4">Transcript</h2>
                <div className="space-y-3 max-h-64 overflow-y-auto">
                  {t.segments.map((seg, i) => (
                    <div key={i} className="flex gap-3 text-sm">
                      <span className="text-gray-400 font-mono flex-shrink-0">{formatDuration(seg.time)}</span>
                      <span className="text-gray-600 dark:text-gray-400">{seg.text}</span>
                    </div>
                  ))}
                </div>
              </div>
            );
          } catch { return null; }
        })()}
      </div>
    </LearnerDashboardShell>
  );
}
