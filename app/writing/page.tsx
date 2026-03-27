'use client';

import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  PenTool,
  Star,
  PlayCircle,
  FileText,
  Clock,
  Target,
  History,
  Award,
  ArrowRight,
  Calendar,
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Badge, StatusBadge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingTasks, fetchWritingSubmissions, fetchBilling } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import type { WritingTask, WritingSubmission } from '@/lib/mock-data';

type TabType = 'practice' | 'drills' | 'past';

const filterGroups: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [
    { id: 'Nursing', label: 'Nursing' }, { id: 'Medicine', label: 'Medicine' },
    { id: 'Physiotherapy', label: 'Physiotherapy' }, { id: 'Dietetics', label: 'Dietetics' },
  ]},
  { id: 'difficulty', label: 'Difficulty', options: [
    { id: 'Easy', label: 'Easy' }, { id: 'Medium', label: 'Medium' }, { id: 'Hard', label: 'Hard' },
  ]},
];

export default function WritingHome() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<TabType>('practice');
  const [tasks, setTasks] = useState<WritingTask[]>([]);
  const [submissions, setSubmissions] = useState<WritingSubmission[]>([]);
  const [credits, setCredits] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filters, setFilters] = useState<Record<string, string[]>>({});

  useEffect(() => {
    analytics.track('module_entry', { module: 'writing' });
    Promise.all([fetchWritingTasks(), fetchWritingSubmissions(), fetchBilling()])
      .then(([t, s, b]) => { setTasks(t); setSubmissions(s); setCredits(b.reviewCredits); })
      .catch(() => setError('Failed to load writing tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, []);

  const handleFilterChange = useCallback((groupId: string, optionId: string) => {
    setFilters(prev => {
      const current = prev[groupId] ?? [];
      return { ...prev, [groupId]: current.includes(optionId) ? current.filter(id => id !== optionId) : [...current, optionId] };
    });
  }, []);

  const filteredTasks = tasks.filter(t => {
    if (filters.profession?.length && !filters.profession.includes(t.profession)) return false;
    if (filters.difficulty?.length && !filters.difficulty.includes(t.difficulty)) return false;
    return true;
  });

  const recommended = tasks[0];

  if (loading) {
    return (
      <AppShell pageTitle="Writing">
        <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-6">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <Skeleton className="h-56 rounded-2xl" />
            <Skeleton className="h-56 rounded-2xl" />
          </div>
          <Skeleton className="h-10 w-80" />
          {[1,2,3].map(i => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      </AppShell>
    );
  }

  if (error) {
    return (
      <AppShell pageTitle="Writing">
        <div className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle="Writing">
      {/* Header */}
      <header className="bg-navy text-white pt-10 pb-20 px-4 sm:px-6 lg:px-8 relative overflow-hidden">
        <div className="absolute inset-0 opacity-10 bg-[radial-gradient(circle_at_top_right,_var(--tw-gradient-stops))] from-amber-400 via-navy to-navy"></div>
        <div className="max-w-6xl mx-auto relative z-10">
          <div className="flex flex-col md:flex-row md:items-end justify-between gap-6">
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
              <div className="flex items-center gap-3 mb-2">
                <div className="w-10 h-10 rounded-lg bg-amber-500/20 flex items-center justify-center">
                  <PenTool className="w-5 h-5 text-amber-400" />
                </div>
                <h1 className="text-3xl sm:text-4xl font-bold">Writing</h1>
              </div>
              <p className="text-gray-300 text-lg">Master your clinical letter writing skills.</p>
            </motion.div>
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.1 }}>
              <div className="bg-white/10 backdrop-blur-sm border border-white/20 rounded-xl p-4 flex items-center gap-4">
                <div className="w-10 h-10 rounded-lg bg-yellow-500/20 flex items-center justify-center shrink-0">
                  <Star className="w-5 h-5 text-yellow-400 fill-yellow-400" />
                </div>
                <div>
                  <div className="text-xs text-gray-300 uppercase tracking-wider font-semibold mb-0.5">Expert Reviews</div>
                  <div className="font-bold text-white flex items-baseline gap-2">
                    {credits} Credits <span className="text-sm font-normal text-gray-400">Available</span>
                  </div>
                </div>
              </div>
            </motion.div>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-4 sm:px-6 lg:px-8 -mt-10 relative z-20">
        {/* Top Action Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
          {/* Recommended Task */}
          {recommended && (
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.2 }}>
              <Card className="p-6 flex flex-col justify-between h-full">
                <div>
                  <div className="inline-flex items-center gap-1.5 bg-amber-100 text-amber-700 px-2.5 py-1 rounded text-xs font-bold uppercase tracking-wider mb-4 border border-amber-200">
                    <Star className="w-3.5 h-3.5 fill-amber-700" /> Recommended Next
                  </div>
                  <h2 className="text-xl font-bold text-navy mb-2">{recommended.title}</h2>
                  <p className="text-sm text-muted mb-4">Focuses on {recommended.criteriaFocus}, your current priority area.</p>
                  <div className="flex items-center gap-4 text-sm font-semibold text-gray-500 mb-6">
                    <span className="flex items-center gap-1.5"><FileText className="w-4 h-4" /> {recommended.scenarioType}</span>
                    <span className="flex items-center gap-1.5"><Clock className="w-4 h-4" /> {recommended.time}</span>
                  </div>
                </div>
                <Button fullWidth onClick={() => { analytics.track('task_started', { taskId: recommended.id, subtest: 'writing' }); router.push(`/writing/player?taskId=${recommended.id}`); }}>
                  <PlayCircle className="w-5 h-5" /> Start Recommended Task
                </Button>
              </Card>
            </motion.div>
          )}

          {/* Full Mock Entry */}
          <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.3 }}>
            <div className="bg-gradient-to-br from-navy to-navy-light rounded-2xl shadow-sm border border-gray-800 p-6 text-white flex flex-col justify-between h-full">
              <div>
                <div className="inline-flex items-center gap-1.5 bg-white/10 px-2.5 py-1 rounded text-xs font-bold uppercase tracking-wider text-primary-light mb-4">
                  <Award className="w-3.5 h-3.5" /> Exam Simulation
                </div>
                <h2 className="text-xl font-bold mb-2">Full Writing Mock Test</h2>
                <p className="text-gray-300 text-sm mb-6">Experience a complete, timed writing sub-test under official exam conditions.</p>
              </div>
              <button onClick={() => router.push('/mocks/setup')} className="w-full bg-white text-navy hover:bg-gray-50 px-6 py-3 rounded-xl font-bold transition-colors shadow-sm flex items-center justify-center gap-2">
                Enter Mock Flow <ArrowRight className="w-5 h-5" />
              </button>
            </div>
          </motion.div>
        </div>

        {/* Library & History Section */}
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.4 }}>
          {/* Tabs */}
          <div className="flex overflow-x-auto border-b border-gray-200 mb-6">
            {([
              { key: 'practice' as TabType, label: 'Practice Library', icon: FileText },
              { key: 'drills' as TabType, label: 'Criterion Drills', icon: Target },
              { key: 'past' as TabType, label: 'Past Submissions', icon: History },
            ]).map(tab => (
              <button key={tab.key} onClick={() => setActiveTab(tab.key)}
                className={`whitespace-nowrap py-4 px-6 font-bold text-sm border-b-2 transition-colors flex items-center gap-2 ${activeTab === tab.key ? 'border-primary text-primary' : 'border-transparent text-muted hover:text-navy'}`}>
                <tab.icon className="w-4 h-4" /> {tab.label}
              </button>
            ))}
          </div>

          {/* Filters */}
          <AnimatePresence mode="wait">
            {activeTab !== 'past' && (
              <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: 'auto' }} exit={{ opacity: 0, height: 0 }}>
                <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange}
                  onClear={() => setFilters({})} className="mb-6 bg-white p-4 rounded-xl border border-gray-200 shadow-sm" />
              </motion.div>
            )}
          </AnimatePresence>

          {/* Tab Content */}
          <div className="bg-white rounded-2xl shadow-sm border border-gray-200 overflow-hidden">
            <AnimatePresence mode="wait">
              {activeTab === 'practice' && (
                <motion.div key="practice" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {filteredTasks.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title="No tasks match your filters" description="Try removing some filters to see more tasks." />
                    </div>
                  ) : filteredTasks.map(task => (
                    <div key={task.id} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => { analytics.track('task_started', { taskId: task.id, subtest: 'writing' }); router.push(`/writing/player?taskId=${task.id}`); }}>
                      <div>
                        <h3 className="font-bold text-navy mb-1 group-hover:text-primary transition-colors">{task.title}</h3>
                        <div className="flex flex-wrap items-center gap-2 mt-1">
                          <Badge variant="muted" size="sm">{task.profession}</Badge>
                          <Badge variant={task.difficulty === 'Hard' ? 'danger' : task.difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{task.difficulty}</Badge>
                          <Badge variant="info" size="sm">{task.scenarioType}</Badge>
                        </div>
                      </div>
                      <Button size="sm" variant="outline">Start Practice</Button>
                    </div>
                  ))}
                </motion.div>
              )}

              {activeTab === 'drills' && (
                <motion.div key="drills" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {filteredTasks.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title="No drills match your filters" description="Try removing some filters to see more drills." />
                    </div>
                  ) : filteredTasks.map(task => (
                    <div key={task.id} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer">
                      <div>
                        <div className="flex items-center gap-2 mb-1">
                          <Target className="w-4 h-4 text-amber-500" />
                          <h3 className="font-bold text-navy group-hover:text-primary transition-colors">{task.title}</h3>
                        </div>
                        <div className="flex flex-wrap items-center gap-2 mt-1">
                          <Badge variant="warning" size="sm">Focus: {task.criteriaFocus}</Badge>
                          <Badge variant={task.difficulty === 'Hard' ? 'danger' : task.difficulty === 'Medium' ? 'warning' : 'success'} size="sm">{task.difficulty}</Badge>
                        </div>
                      </div>
                      <Button size="sm" variant="outline">Start Drill</Button>
                    </div>
                  ))}
                </motion.div>
              )}

              {activeTab === 'past' && (
                <motion.div key="past" initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 10 }} className="divide-y divide-gray-100">
                  {submissions.length === 0 ? (
                    <div className="p-10">
                      <EmptyState title="No submissions yet" description="Complete a writing task to see your history here." />
                    </div>
                  ) : submissions.map(sub => (
                    <div key={sub.id} className="p-5 hover:bg-gray-50 transition-colors flex flex-col sm:flex-row sm:items-center justify-between gap-4 group cursor-pointer"
                      onClick={() => router.push(`/writing/result?id=${sub.id}`)}>
                      <div>
                        <h3 className="font-bold text-navy mb-1 group-hover:text-primary transition-colors">{sub.taskTitle}</h3>
                        <div className="flex items-center gap-3 text-sm text-muted">
                          <span className="flex items-center gap-1"><Calendar className="w-4 h-4" /> {new Date(sub.submittedAt).toLocaleDateString()}</span>
                          {sub.scoreEstimate && <span className="font-semibold text-navy">{sub.scoreEstimate}</span>}
                        </div>
                      </div>
                      <div className="flex items-center gap-3 shrink-0">
                        <StatusBadge status={sub.evalStatus === 'completed' ? 'completed' : sub.evalStatus === 'processing' ? 'processing' : 'queued'} />
                        <Button size="sm" variant="ghost">View <ArrowRight className="w-4 h-4" /></Button>
                      </div>
                    </div>
                  ))}
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </motion.div>
      </main>
    </AppShell>
  );
}
