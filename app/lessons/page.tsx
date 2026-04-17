'use client';

import { useEffect, useState } from 'react';
import { Video, Clock, PlayCircle, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import Image from 'next/image';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { fetchVideoLessons } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type VideoLesson = { id: string; examTypeCode: string; subtestCode: string | null; title: string; description: string | null; durationSeconds: number; thumbnailUrl: string | null; difficultyLevel: string; status: string };

const SUBTEST_LABELS: Record<string, string> = { writing: 'Writing', speaking: 'Speaking', reading: 'Reading', listening: 'Listening' };

function formatDuration(seconds: number) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

export default function LessonsPage() {
  const [lessons, setLessons] = useState<VideoLesson[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [examType, setExamType] = useState('oet');
  const [subtest, setSubtest] = useState('');
  const heroHighlights = [
    { icon: PlayCircle, label: 'Format', value: 'Video lessons' },
    { icon: Clock, label: 'Filter', value: subtest || 'All subtests' },
    { icon: ChevronRight, label: 'Library', value: 'Guided practice' },
  ];

  useEffect(() => {
    analytics.track('lessons_page_viewed');
  }, []);

  useEffect(() => {
    fetchVideoLessons({ examTypeCode: examType, subtestCode: subtest || undefined }).then(data => {
      setLessons(data as VideoLesson[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load video lessons.');
      setLoading(false);
    });
  }, [examType, subtest]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
      <LearnerPageHero
        eyebrow="Learn"
        title="Video Lessons"
        description="Watch expert-led OET preparation lessons."
        icon={Video}
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <Card className="p-5 shadow-sm">
        <LearnerSurfaceSectionHeader
          eyebrow="Library filters"
          title="Narrow the lesson set"
          description="Filter lessons by exam type and sub-test."
          className="mb-4"
        />
        <div className="flex flex-wrap gap-3">
          <select value={examType} onChange={e => {
            setError(null);
            setLoading(true);
            setExamType(e.target.value);
          }} className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2">
            <option value="oet">OET</option>
            <option value="ielts">IELTS</option>
            <option value="pte">PTE</option>
          </select>
          <select value={subtest} onChange={e => {
            setError(null);
            setLoading(true);
            setSubtest(e.target.value);
          }} className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2">
            <option value="">All Subtests</option>
            <option value="writing">Writing</option>
            <option value="speaking">Speaking</option>
            <option value="reading">Reading</option>
            <option value="listening">Listening</option>
          </select>
        </div>
      </Card>

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-52 rounded-3xl" />)}
        </div>
      ) : lessons.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <p className="text-sm text-muted">No video lessons available for this selection.</p>
        </Card>
      ) : (
        <div>
          <LearnerSurfaceSectionHeader
            eyebrow="Lessons"
            title="Curated video lessons"
            description="Expert-led preparation content to strengthen your skills."
            className="mb-4"
          />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {lessons.map((lesson, i) => (
            <MotionItem key={lesson.id} delayIndex={i}>
              <Link href={`/lessons/${lesson.id}`}>
                <div className="group overflow-hidden rounded-3xl border border-border bg-surface shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
                  <div className="relative flex h-36 items-center justify-center bg-gradient-to-br from-primary/90 to-indigo-600">
                    {lesson.thumbnailUrl ? (
                      <Image
                        src={lesson.thumbnailUrl}
                        alt={lesson.title}
                        fill
                        unoptimized
                        sizes="(max-width: 1024px) 100vw, 33vw"
                        className="object-cover"
                      />
                    ) : (
                      <PlayCircle className="h-14 w-14 text-white/70 transition-colors group-hover:text-white" />
                    )}
                    <div className="absolute bottom-2 right-2 flex items-center gap-1 rounded-full bg-black/60 px-2 py-1 text-xs text-white">
                      <Clock className="h-3 w-3" />
                      {formatDuration(lesson.durationSeconds)}
                    </div>
                  </div>
                  <div className="p-5">
                    <div className="flex items-center gap-2 mb-1.5">
                      {lesson.subtestCode && (
                        <span className="rounded-full bg-primary/10 px-2 py-0.5 text-xs capitalize text-primary-dark">
                          {SUBTEST_LABELS[lesson.subtestCode] ?? lesson.subtestCode}
                        </span>
                      )}
                      <span className="text-xs capitalize text-muted">{lesson.difficultyLevel}</span>
                    </div>
                    <div className="line-clamp-2 text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">{lesson.title}</div>
                    {lesson.description && <div className="mt-1 line-clamp-2 text-xs text-muted">{lesson.description}</div>}
                  </div>
                </div>
              </Link>
            </MotionItem>
          ))}
          </div>
        </div>
      )}
      </div>
    </LearnerDashboardShell>
  );
}
