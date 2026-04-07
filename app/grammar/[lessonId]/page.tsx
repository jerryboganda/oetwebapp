'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { BookMarked, ArrowLeft, Clock, CheckCircle2 } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchGrammarLesson, startGrammarLesson, completeGrammarLesson } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type GrammarLesson = {
  id: string;
  title: string;
  description: string | null;
  level: string;
  estimatedMinutes: number;
  contentHtml: string | null;
  exercisesJson: string | null;
};

type GrammarExercise = { question: string; options?: string[]; answer: string };

function parseExercises(value: string | null | undefined): GrammarExercise[] {
  if (!value) return [];

  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];

    return parsed.filter((exercise): exercise is GrammarExercise => {
      if (!exercise || typeof exercise !== 'object') return false;
      const item = exercise as Record<string, unknown>;
      return typeof item.question === 'string' && typeof item.answer === 'string';
    });
  } catch {
    return [];
  }
}

export default function GrammarLessonPage() {
  const params = useParams<{ lessonId: string }>();
  const [lesson, setLesson] = useState<GrammarLesson | null>(null);
  const [exercises, setExercises] = useState<GrammarExercise[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [started, setStarted] = useState(false);
  const [completed, setCompleted] = useState(false);
  const [score, setScore] = useState<number | null>(null);
  const [answers, setAnswers] = useState<Record<number, string>>({});
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (!params.lessonId) return;
    let cancelled = false;

    (async () => {
      setLoading(true);
      setError(null);

      try {
        const data = await fetchGrammarLesson(params.lessonId);
        if (cancelled) return;

        const l = data as GrammarLesson;
        setLesson(l);
        setExercises(parseExercises(l.exercisesJson));
        setStarted(false);
        setCompleted(false);
        setScore(null);
        setAnswers({});
        analytics.track('grammar_lesson_viewed', { lessonId: l.id });
      } catch {
        if (!cancelled) {
          setError('Could not load lesson.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [params.lessonId]);

  async function handleStart() {
    if (!lesson) return;
    try {
      await startGrammarLesson(lesson.id);
      setStarted(true);
    } catch {
      setError('Could not start lesson.');
    }
  }

  async function handleComplete() {
    if (!lesson || submitting) return;
    let correct = 0;
    exercises.forEach((ex, i) => {
      if (answers[i]?.trim().toLowerCase() === ex.answer.toLowerCase()) correct++;
    });
    const finalScore = exercises.length > 0 ? Math.round((correct / exercises.length) * 100) : 100;
    setSubmitting(true);
    try {
      await completeGrammarLesson(lesson.id, finalScore, JSON.stringify(answers));
      setScore(finalScore);
      setCompleted(true);
    } catch {
      setError('Could not save completion.');
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-8 w-48 mb-4 rounded" />
        <Skeleton className="h-64 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!lesson) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">Lesson not found.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/grammar" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div>
          <div className="flex items-center gap-2">
            <BookMarked className="w-5 h-5 text-purple-500" />
            <span className="text-xs text-gray-400 uppercase font-medium">{lesson.level}</span>
            <span className="text-xs text-gray-400 flex items-center gap-1"><Clock className="w-3 h-3" />{lesson.estimatedMinutes} min</span>
          </div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-white">{lesson.title}</h1>
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {completed ? (
        <motion.div initial={{ opacity: 0, scale: 0.95 }} animate={{ opacity: 1, scale: 1 }} className="max-w-md mx-auto text-center py-16">
          <CheckCircle2 className="w-16 h-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-2xl font-bold text-gray-900 dark:text-white mb-2">Lesson Complete!</h2>
          <div className="text-5xl font-bold text-indigo-600 dark:text-indigo-400 mb-2">{score}%</div>
          <p className="text-gray-500 mb-6">Great work on this grammar topic.</p>
          <Link href="/grammar" className="px-6 py-2.5 bg-purple-600 hover:bg-purple-700 text-white rounded-xl font-medium inline-block">
            Back to Grammar Lessons
          </Link>
        </motion.div>
      ) : (
        <div className="max-w-2xl mx-auto">
          {/* Intro */}
          {lesson.description && (
            <div className="bg-purple-50 dark:bg-purple-900/20 rounded-xl p-4 mb-6 text-purple-800 dark:text-purple-200 text-sm">
              {lesson.description}
            </div>
          )}

          {!started ? (
            <div className="text-center py-8">
              <button onClick={handleStart} className="px-8 py-3 bg-purple-600 hover:bg-purple-700 text-white font-semibold rounded-xl transition-colors">
                Start Lesson
              </button>
            </div>
          ) : (
            <div className="space-y-6">
              {lesson.contentHtml && (
                <article
                  className="prose max-w-none rounded-2xl border border-gray-200 bg-white p-5 text-gray-800 shadow-sm dark:prose-invert dark:border-gray-700 dark:bg-gray-800 dark:text-gray-200"
                  dangerouslySetInnerHTML={{ __html: lesson.contentHtml }}
                />
              )}

              {exercises.length > 0 ? (
                <div>
                  <h3 className="font-semibold text-gray-900 dark:text-white mb-4">Practice Exercises</h3>
                  <div className="space-y-4">
                    {exercises.map((ex, i) => (
                      <div key={i} className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4">
                        <div className="text-sm font-medium text-gray-800 dark:text-gray-200 mb-3">{i + 1}. {ex.question}</div>
                        {ex.options ? (
                          <div className="space-y-2">
                            {ex.options.map((opt, j) => (
                              <button
                                key={j}
                                onClick={() => setAnswers(prev => ({ ...prev, [i]: opt }))}
                                className={`w-full text-left px-3 py-2 rounded-lg text-sm border transition-colors ${answers[i] === opt ? 'border-purple-400 bg-purple-50 dark:bg-purple-900/20 text-purple-800 dark:text-purple-200' : 'border-gray-200 dark:border-gray-700 hover:border-purple-300'}`}
                              >
                                {opt}
                              </button>
                            ))}
                          </div>
                        ) : (
                          <input
                            type="text"
                            placeholder="Your answer..."
                            value={answers[i] ?? ''}
                            onChange={e => setAnswers(prev => ({ ...prev, [i]: e.target.value }))}
                            className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 focus:outline-none focus:ring-2 focus:ring-purple-400"
                          />
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="rounded-2xl border border-dashed border-gray-300 bg-gray-50 p-5 text-sm text-gray-500 dark:border-gray-700 dark:bg-gray-900/40 dark:text-gray-400">
                  No interactive exercises are attached to this lesson yet.
                </div>
              )}

              <div className="pt-4 flex justify-end">
                <button
                  onClick={handleComplete}
                  disabled={submitting}
                  className="px-8 py-3 bg-purple-600 hover:bg-purple-700 text-white font-semibold rounded-xl transition-colors disabled:opacity-50"
                >
                  {submitting ? 'Saving...' : 'Complete Lesson'}
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
