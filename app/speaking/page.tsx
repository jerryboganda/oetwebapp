'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  Mic, Star, Play, AlertCircle, Volume2, Heart, ChevronRight, Clock,
} from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { TaskCard } from '@/components/domain/task-card';
import { fetchSpeakingTasks, fetchBilling, fetchSubmissions } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { SpeakingTask, Submission } from '@/lib/mock-data';

export default function SpeakingHome() {
  const [tasks, setTasks] = useState<SpeakingTask[]>([]);
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [credits, setCredits] = useState(0);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('module_entry', { module: 'speaking' });
    Promise.all([fetchSpeakingTasks(), fetchBilling(), fetchSubmissions()])
      .then(([t, b, s]) => {
        setTasks(t);
        setCredits(b.reviewCredits);
        setSubmissions(s.filter((sub) => sub.subTest === 'Speaking'));
      })
      .finally(() => setLoading(false));
  }, []);

  const recommended = tasks[0];

  if (loading) {
    return (
      <AppShell pageTitle="Speaking">
        <div className="space-y-6">
          <Skeleton className="h-40 w-full rounded-2xl" />
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
            <div className="lg:col-span-2 space-y-4">
              <Skeleton className="h-24 w-full rounded-xl" />
              <Skeleton className="h-24 w-full rounded-xl" />
            </div>
            <Skeleton className="h-48 w-full rounded-xl" />
          </div>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell
      pageTitle="Speaking"
      navActions={
        <div className="flex items-center gap-2 bg-navy text-white px-3 py-1.5 rounded-lg text-xs font-bold">
          <Star className="w-3.5 h-3.5 text-yellow-400 fill-yellow-400" />
          {credits} Expert Credits
        </div>
      }
    >
      <div className="space-y-8">
        {/* Recommended Role Play */}
        {recommended && (
          <section>
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-sm font-bold text-muted uppercase tracking-widest">Recommended Next</h2>
              <Link href="/speaking/selection" className="text-xs font-bold text-primary hover:underline flex items-center gap-1">
                Browse All <ChevronRight className="w-3 h-3" />
              </Link>
            </div>
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
              <Card className="p-6">
                <div className="flex flex-col md:flex-row md:items-center justify-between gap-6">
                  <div className="flex-1">
                    <div className="flex items-center gap-2 mb-2">
                      <Badge variant="info">{recommended.profession}</Badge>
                      <span className="flex items-center gap-1 text-xs text-muted">
                        <Clock className="w-3 h-3" /> {recommended.duration}
                      </span>
                    </div>
                    <h3 className="text-xl font-bold text-navy mb-1">{recommended.title}</h3>
                    <p className="text-muted text-sm">Focus: {recommended.criteriaFocus}</p>
                  </div>
                  <Link href={`/speaking/check?taskId=${recommended.id}`}>
                    <Button>
                      <Play className="w-4 h-4 fill-current" /> Start Practice
                    </Button>
                  </Link>
                </div>
              </Card>
            </motion.div>
          </section>
        )}

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Left: Drills & Tasks */}
          <div className="lg:col-span-2 space-y-8">
            {/* Targeted Drills */}
            <section>
              <h2 className="text-sm font-bold text-muted uppercase tracking-widest mb-4">Targeted Drills</h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <Card hoverable className="p-5 flex items-center gap-4">
                  <div className="w-12 h-12 rounded-xl bg-orange-50 flex items-center justify-center">
                    <Volume2 className="w-6 h-6 text-orange-600" />
                  </div>
                  <div>
                    <h3 className="font-bold text-navy">Pronunciation</h3>
                    <p className="text-xs text-muted">Master tricky medical terms</p>
                  </div>
                </Card>
                <Card hoverable className="p-5 flex items-center gap-4">
                  <div className="w-12 h-12 rounded-xl bg-pink-50 flex items-center justify-center">
                    <Heart className="w-6 h-6 text-pink-600" />
                  </div>
                  <div>
                    <h3 className="font-bold text-navy">Empathy & Clarity</h3>
                    <p className="text-xs text-muted">Soft skills for patient care</p>
                  </div>
                </Card>
              </div>
            </section>

            {/* More Tasks */}
            {tasks.length > 1 && (
              <section>
                <h2 className="text-sm font-bold text-muted uppercase tracking-widest mb-4">More Scenarios</h2>
                <div className="grid grid-cols-1 gap-3">
                  {tasks.slice(1, 4).map((t) => (
                    <TaskCard
                      key={t.id}
                      id={t.id}
                      title={t.title}
                      subtest="Speaking"
                      profession={t.profession}
                      duration={t.duration}
                      difficulty={t.difficulty}
                      tags={[t.scenarioType, t.criteriaFocus]}
                      onStart={() => {
                        analytics.track('task_started', { taskId: t.id, subtest: 'speaking' });
                      }}
                    />
                  ))}
                </div>
              </section>
            )}
          </div>

          {/* Right: Past Attempts */}
          <div className="space-y-8">
            <section>
              <h2 className="text-sm font-bold text-muted uppercase tracking-widest mb-4">Past Attempts</h2>
              {submissions.length === 0 ? (
                <Card className="p-6 text-center">
                  <Mic className="w-8 h-8 text-muted mx-auto mb-2" />
                  <p className="text-sm text-muted">No speaking attempts yet.</p>
                  <Link href="/speaking/selection" className="text-sm font-bold text-primary mt-2 inline-block">Start your first task</Link>
                </Card>
              ) : (
                <div className="space-y-3">
                  {submissions.map((sub) => (
                    <Link key={sub.id} href={`/speaking/results/${sub.id}`}>
                      <Card hoverable className="p-4">
                        <div className="flex items-center justify-between mb-1">
                          <h3 className="text-sm font-bold text-navy truncate pr-2">{sub.taskName}</h3>
                          <Badge variant="muted" size="sm">{sub.scoreEstimate}</Badge>
                        </div>
                        <p className="text-xs text-muted">{sub.attemptDate}</p>
                      </Card>
                    </Link>
                  ))}
                  <Link href="/submissions" className="block text-center text-xs font-bold text-muted hover:text-primary py-2">
                    View Full History
                  </Link>
                </div>
              )}
            </section>
          </div>
        </div>
      </div>
    </AppShell>
  );
}
