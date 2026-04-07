'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Video, Clock, PlayCircle, ChevronRight } from 'lucide-react';
import Link from 'next/link';
import Image from 'next/image';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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
      <LearnerPageHero
        title="Video Lessons"
        description="Watch expert-led OET preparation lessons"
        icon={Video}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="flex flex-wrap gap-3 mb-6">
        <select value={examType} onChange={e => {
          setError(null);
          setLoading(true);
          setExamType(e.target.value);
        }} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="oet">OET</option>
          <option value="ielts">IELTS</option>
          <option value="pte">PTE</option>
        </select>
        <select value={subtest} onChange={e => {
          setError(null);
          setLoading(true);
          setSubtest(e.target.value);
        }} className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300">
          <option value="">All Subtests</option>
          <option value="writing">Writing</option>
          <option value="speaking">Speaking</option>
          <option value="reading">Reading</option>
          <option value="listening">Listening</option>
        </select>
      </div>

      {loading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-52 rounded-xl" />)}
        </div>
      ) : lessons.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No video lessons available for this selection.</div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {lessons.map((lesson, i) => (
            <motion.div key={lesson.id} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.04 }}>
              <Link href={`/lessons/${lesson.id}`}>
                <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 overflow-hidden hover:shadow-md transition-shadow cursor-pointer group">
                  <div className="bg-gradient-to-br from-blue-500 to-indigo-600 h-36 flex items-center justify-center relative">
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
                      <PlayCircle className="w-14 h-14 text-white/70 group-hover:text-white transition-colors" />
                    )}
                    <div className="absolute bottom-2 right-2 bg-black/60 text-white text-xs px-1.5 py-0.5 rounded flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {formatDuration(lesson.durationSeconds)}
                    </div>
                  </div>
                  <div className="p-4">
                    <div className="flex items-center gap-2 mb-1.5">
                      {lesson.subtestCode && (
                        <span className="text-xs px-2 py-0.5 rounded-full bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 capitalize">
                          {SUBTEST_LABELS[lesson.subtestCode] ?? lesson.subtestCode}
                        </span>
                      )}
                      <span className="text-xs text-gray-400 capitalize">{lesson.difficultyLevel}</span>
                    </div>
                    <div className="font-semibold text-gray-900 dark:text-white text-sm group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors line-clamp-2">{lesson.title}</div>
                    {lesson.description && <div className="text-xs text-gray-500 mt-1 line-clamp-2">{lesson.description}</div>}
                  </div>
                </div>
              </Link>
            </motion.div>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
