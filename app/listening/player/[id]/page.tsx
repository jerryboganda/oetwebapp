'use client';

import { useState, useRef, useEffect, Suspense } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion } from 'motion/react';
import { Play, Pause, Volume2, AlertCircle, CheckCircle2, Loader2 } from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchListeningTask, submitListeningAnswers } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ListeningTask } from '@/lib/mock-data';

function formatTime(seconds: number) {
  if (!seconds || isNaN(seconds)) return '00:00';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

function PlayerContent() {
  const params = useParams();
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = params?.id as string;
  const mode = (searchParams?.get('mode') as 'practice' | 'exam') || 'practice';
  const isExam = mode === 'exam';

  const audioRef = useRef<HTMLAudioElement>(null);
  const [task, setTask] = useState<ListeningTask | null>(null);
  const [loadingTask, setLoadingTask] = useState(true);
  const [isPlaying, setIsPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [duration, setDuration] = useState(0);
  const [hasStarted, setHasStarted] = useState(false);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    fetchListeningTask(id)
      .then(t => {
        setTask(t);
        analytics.track('task_started', { subtest: 'listening', taskId: id });
      })
      .finally(() => setLoadingTask(false));
  }, [id]);

  const togglePlayPause = () => {
    if (isExam && hasStarted) return;
    if (audioRef.current) {
      if (isPlaying) audioRef.current.pause();
      else audioRef.current.play().catch(console.error);
    }
  };

  const handleScrub = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (isExam) return;
    const newTime = Number(e.target.value);
    if (audioRef.current) {
      audioRef.current.currentTime = newTime;
      setProgress(newTime);
    }
  };

  const startTask = () => {
    setHasStarted(true);
    if (audioRef.current) audioRef.current.play().catch(console.error);
  };

  const handleAnswerChange = (questionId: string, answer: string) => {
    setAnswers(prev => ({ ...prev, [questionId]: answer }));
  };

  const handleSubmit = async () => {
    if (!task) return;
    if (!window.confirm('Are you sure you want to submit your answers?')) return;
    setIsSubmitting(true);
    if (audioRef.current) audioRef.current.pause();
    try {
      await submitListeningAnswers(task.id, answers);
      analytics.track('task_submitted', { subtest: 'listening', taskId: task.id });
      router.push(`/listening/results/${task.id}`);
    } catch {
      setIsSubmitting(false);
    }
  };

  if (loadingTask) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-6">
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-48 rounded-[24px]" />
          <Skeleton className="h-48 rounded-[24px]" />
        </div>
      </AppShell>
    );
  }

  if (!task) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
          <AlertCircle className="w-12 h-12 text-rose-500" />
          <h2 className="text-xl font-black text-navy">Task not found</h2>
          <Link href="/listening"><Button variant="ghost">Back to Listening</Button></Link>
        </div>
      </AppShell>
    );
  }

  if (isSubmitting) {
    return (
      <AppShell pageTitle="Submitting" distractionFree>
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
          <Loader2 className="w-12 h-12 text-primary animate-spin" />
          <h2 className="text-xl font-black text-navy">Submitting Answers...</h2>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle={task.title} distractionFree>
      {/* Hidden Audio Element */}
      <audio
        ref={audioRef}
        src={task.audioSrc}
        onTimeUpdate={() => audioRef.current && setProgress(audioRef.current.currentTime)}
        onLoadedMetadata={() => audioRef.current && setDuration(audioRef.current.duration)}
        onPlay={() => setIsPlaying(true)}
        onPause={() => setIsPlaying(false)}
        onEnded={() => setIsPlaying(false)}
        preload="metadata"
      />

      <div className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 pb-24">
        {!hasStarted ? (
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className="bg-surface rounded-[32px] border border-gray-200 p-8 sm:p-12 text-center shadow-sm mt-8"
          >
            <div className="w-20 h-20 bg-blue-50 rounded-full flex items-center justify-center mx-auto mb-6">
              <Volume2 className="w-10 h-10 text-blue-500" />
            </div>
            <h2 className="text-2xl font-black text-navy mb-4">Ready to start?</h2>
            <div className="bg-gray-50 rounded-2xl p-6 mb-8 text-left space-y-4 max-w-lg mx-auto">
              <h3 className="text-sm font-black text-muted uppercase tracking-widest">Instructions</h3>
              <ul className="space-y-3 text-sm text-gray-600">
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="w-5 h-5 text-green-500 shrink-0" />
                  <span>Ensure your volume is turned up and you are in a quiet place.</span>
                </li>
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="w-5 h-5 text-green-500 shrink-0" />
                  <span>Select the best answer for each question as you listen.</span>
                </li>
                {isExam ? (
                  <li className="flex items-start gap-2">
                    <AlertCircle className="w-5 h-5 text-rose-500 shrink-0" />
                    <span className="font-bold text-rose-700">Exam Mode: The audio will play only once. You cannot pause or rewind.</span>
                  </li>
                ) : (
                  <li className="flex items-start gap-2">
                    <CheckCircle2 className="w-5 h-5 text-green-500 shrink-0" />
                    <span>Practice Mode: You can pause and scrub the audio as needed.</span>
                  </li>
                )}
              </ul>
            </div>
            <Button size="lg" onClick={startTask} className="gap-2">
              <Play className="w-5 h-5" /> Start Audio &amp; Task
            </Button>
          </motion.div>
        ) : (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="space-y-8">
            {/* Sticky Audio Player */}
            <div className="bg-navy text-white p-4 sm:p-5 rounded-2xl flex items-center gap-4 sticky top-20 z-20 shadow-xl shadow-navy/10">
              <button
                onClick={togglePlayPause}
                disabled={isExam && hasStarted}
                aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
                className={`w-12 h-12 rounded-full flex items-center justify-center shrink-0 transition-colors ${
                  isExam && hasStarted
                    ? 'bg-white/10 text-white/30 cursor-not-allowed'
                    : 'bg-white text-navy hover:bg-gray-100'
                }`}
              >
                {isPlaying ? <Pause className="w-6 h-6" /> : <Play className="w-6 h-6 ml-1" />}
              </button>
              <div className="flex-1 flex flex-col gap-1.5">
                <div className="flex justify-between text-xs font-bold text-white/70 font-mono">
                  <span>{formatTime(progress)}</span>
                  <span>{formatTime(duration)}</span>
                </div>
                <div className="relative w-full h-2.5 bg-white/20 rounded-full overflow-hidden">
                  <div
                    className="absolute top-0 left-0 h-full bg-blue-400 transition-all duration-100 ease-linear"
                    style={{ width: `${duration > 0 ? (progress / duration) * 100 : 0}%` }}
                  />
                  {!isExam && (
                    <input
                      type="range"
                      min="0"
                      max={duration || 100}
                      value={progress}
                      onChange={handleScrub}
                      className="absolute top-0 left-0 w-full h-full opacity-0 cursor-pointer"
                    />
                  )}
                </div>
              </div>
              <Volume2 className="w-5 h-5 text-white/50 shrink-0 hidden sm:block" />
            </div>

            {/* Questions List */}
            <div className="space-y-6">
              {task.questions.map((q) => (
                <div key={q.id} className="bg-surface p-6 sm:p-8 rounded-[24px] border border-gray-200 shadow-sm">
                  <h3 className="text-lg font-medium text-navy mb-6 leading-relaxed">
                    <span className="text-xs font-black text-muted uppercase tracking-widest block mb-2">Question {q.number}</span>
                    {q.text}
                  </h3>
                  <div className="space-y-3">
                    {(q.options || []).map((opt, idx) => {
                      const isSelected = answers[q.id] === opt;
                      return (
                        <button
                          key={idx}
                          onClick={() => handleAnswerChange(q.id, opt)}
                          className={`w-full text-left p-4 sm:p-5 rounded-xl border-2 transition-all ${
                            isSelected
                              ? 'border-primary bg-primary/5 text-primary font-medium'
                              : 'border-gray-100 hover:border-gray-200 text-gray-700 hover:bg-gray-50'
                          }`}
                        >
                          <div className="flex items-center gap-4">
                            <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center shrink-0 transition-colors ${
                              isSelected ? 'border-primary' : 'border-gray-300'
                            }`}>
                              {isSelected && <div className="w-2.5 h-2.5 rounded-full bg-primary" />}
                            </div>
                            <span className="leading-relaxed">{opt}</span>
                          </div>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>

            {/* Submit Section */}
            <div className="flex justify-end pt-4">
              <Button onClick={handleSubmit} size="lg">Submit Answers</Button>
            </div>
          </motion.div>
        )}
      </div>
    </AppShell>
  );
}

export default function ListeningPlayer() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <PlayerContent />
    </Suspense>
  );
}
