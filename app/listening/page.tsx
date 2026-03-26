'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Headphones, ArrowRight, Clock, Target, Volume2, FileText, Sparkles } from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { fetchListeningHome, fetchMockReports } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';

interface ListeningHomeTask {
  contentId: string;
  title: string;
  estimatedDurationMinutes: number;
  difficulty: string;
  scenarioType?: string;
}

export default function ListeningHome() {
  const [tasks, setTasks] = useState<ListeningHomeTask[]>([]);
  const [mockReports, setMockReports] = useState<MockReport[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    analytics.track('module_entry', { module: 'listening' });

    (async () => {
      try {
        const [home, reports] = await Promise.all([fetchListeningHome(), fetchMockReports()]);
        if (cancelled) return;
        setTasks((home.featuredTasks ?? []) as ListeningHomeTask[]);
        setMockReports(reports);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load listening practice.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => { cancelled = true; };
  }, []);

  return (
    <AppShell pageTitle="Listening" subtitle="Strengthen audio comprehension, exact detail capture, and distractor control.">
      <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-10 pb-20">
        <section className="bg-surface rounded-[28px] border border-gray-200 p-6 sm:p-8 shadow-sm">
          <div className="flex items-start gap-4">
            <div className="w-14 h-14 rounded-2xl bg-indigo-50 text-indigo-600 flex items-center justify-center shrink-0">
              <Headphones className="w-7 h-7" />
            </div>
            <div>
              <h1 className="text-2xl sm:text-3xl font-black text-navy">Listening Practice Center</h1>
              <p className="text-sm text-muted mt-2 max-w-2xl">
                Practise consultation audio, sharpen detail capture, and review mock evidence without relying on hard-coded placeholder flows.
              </p>
            </div>
          </div>
        </section>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <section>
          <div className="flex items-center justify-between mb-5">
            <div>
              <h2 className="text-sm font-black text-muted uppercase tracking-widest">Practice Tasks</h2>
              <p className="text-sm text-muted mt-1">Backend-backed listening tasks with stable media metadata.</p>
            </div>
          </div>

          {loading ? (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {[1, 2].map((i) => <Skeleton key={i} className="h-48 rounded-[24px]" />)}
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {tasks.map((task, index) => (
                <motion.div
                  key={task.contentId}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: index * 0.08 }}
                >
                  <Link href={`/listening/player/${task.contentId}`} className="block h-full">
                    <Card className="h-full p-6 hover:shadow-md hover:border-primary/30 transition-all group cursor-pointer">
                      <div className="flex items-start justify-between gap-4 mb-5">
                        <div>
                          <h3 className="text-lg font-black text-navy group-hover:text-primary transition-colors">{task.title}</h3>
                          <p className="text-xs font-bold uppercase tracking-widest text-muted mt-1">
                            {task.scenarioType ? task.scenarioType.replace(/_/g, ' ') : 'Listening Task'}
                          </p>
                        </div>
                        <div className="w-10 h-10 rounded-xl bg-indigo-50 text-indigo-600 flex items-center justify-center shrink-0">
                          <Volume2 className="w-5 h-5" />
                        </div>
                      </div>
                      <div className="flex items-center gap-4 text-xs font-bold text-muted mb-6">
                        <span className="flex items-center gap-1"><Clock className="w-3 h-3" /> {task.estimatedDurationMinutes} mins</span>
                        <span className="px-2 py-1 rounded-md bg-gray-100 text-gray-700">{task.difficulty}</span>
                      </div>
                      <div className="inline-flex items-center gap-2 text-sm font-bold text-primary group-hover:gap-3 transition-all">
                        Start Task <ArrowRight className="w-4 h-4" />
                      </div>
                    </Card>
                  </Link>
                </motion.div>
              ))}
            </div>
          )}
        </section>

        <section className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          <Card className="p-6 sm:p-8">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-xl bg-rose-50 text-rose-600 flex items-center justify-center">
                <Target className="w-5 h-5" />
              </div>
              <h2 className="text-lg font-black text-navy">Distractor Focus</h2>
            </div>
            <ul className="space-y-3 text-sm text-navy/80">
              <li>Listen for exact frequencies, not approximate impressions.</li>
              <li>Separate the patient&apos;s concern from the clinician&apos;s recommendation.</li>
              <li>Use transcript-backed review after errors instead of replaying blindly.</li>
            </ul>
          </Card>

          <Card className="p-6 sm:p-8">
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-xl bg-amber-50 text-amber-600 flex items-center justify-center">
                <Sparkles className="w-5 h-5" />
              </div>
              <h2 className="text-lg font-black text-navy">Recommended Next Step</h2>
            </div>
            <p className="text-sm text-muted leading-relaxed mb-6">
              Start one listening task, then review the latest mock report to see whether your distractor handling is improving over time.
            </p>
            <Button onClick={() => window.location.assign('/mocks')}>
              Open Mock Center <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </Card>
        </section>

        <section>
          <div className="flex items-center justify-between mb-5">
            <div>
              <h2 className="text-sm font-black text-muted uppercase tracking-widest">Recent Mock Reports</h2>
              <p className="text-sm text-muted mt-1">Check listening impact inside full OET mock performance.</p>
            </div>
            <Link href="/mocks" className="text-sm font-bold text-primary hover:underline">Open Mock Center</Link>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {mockReports.slice(0, 2).map((report, index) => (
              <motion.div
                key={report.id}
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.15 + index * 0.08 }}
              >
                <Link href={`/mocks/report/${report.id}`} className="block h-full">
                  <Card className="h-full p-6 hover:shadow-md hover:border-primary/30 transition-all group cursor-pointer">
                    <div className="flex items-start justify-between gap-4 mb-4">
                      <div>
                        <h3 className="text-lg font-black text-navy group-hover:text-primary transition-colors">{report.title}</h3>
                        <p className="text-xs text-muted mt-1">{report.date}</p>
                      </div>
                      <div className="w-10 h-10 rounded-xl bg-gray-50 text-gray-600 flex items-center justify-center shrink-0">
                        <FileText className="w-5 h-5" />
                      </div>
                    </div>
                    <p className="text-sm text-muted leading-relaxed mb-5">{report.summary}</p>
                    <div className="text-sm font-bold text-primary inline-flex items-center gap-2 group-hover:gap-3 transition-all">
                      View Report <ArrowRight className="w-4 h-4" />
                    </div>
                  </Card>
                </Link>
              </motion.div>
            ))}
          </div>
        </section>
      </div>
    </AppShell>
  );
}
