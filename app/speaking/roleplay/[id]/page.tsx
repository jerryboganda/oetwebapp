'use client';

import { useState, useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { motion } from 'motion/react';
import {
  FileText, Edit3, Play, Info, Mic, Bot, User, ShieldCheck,
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { Timer } from '@/components/ui/timer';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Textarea } from '@/components/ui/form-controls';
import { fetchRoleCard } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { RoleCard } from '@/lib/mock-data';

type TaskMode = 'ai' | 'self' | 'exam';

export default function RoleCardPreview() {
  const params = useParams();
  const router = useRouter();
  const id = params?.id as string;

  const [card, setCard] = useState<RoleCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [notes, setNotes] = useState('');
  const [selectedMode, setSelectedMode] = useState<TaskMode>('ai');
  const [prepRunning, setPrepRunning] = useState(true);

  useEffect(() => {
    fetchRoleCard(id)
      .then(setCard)
      .catch(() => setCard(null))
      .finally(() => setLoading(false));
  }, [id]);

  const handleStartTask = () => {
    analytics.track('task_started', { taskId: id, subtest: 'speaking', mode: selectedMode });
    router.push(`/speaking/task/${id}?mode=${selectedMode}`);
  };

  if (loading) {
    return (
      <AppShell pageTitle="Role Card">
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
          <Skeleton className="h-96 rounded-xl" />
          <Skeleton className="h-96 rounded-xl" />
        </div>
      </AppShell>
    );
  }

  if (!card) {
    return (
      <AppShell pageTitle="Role Card">
        <InlineAlert variant="error">Role card not found for this task.</InlineAlert>
      </AppShell>
    );
  }

  return (
    <AppShell
      pageTitle={card.title}
      navActions={
        <Timer
          mode="countdown"
          initialSeconds={180}
          running={prepRunning}
          onComplete={() => setPrepRunning(false)}
          size="md"
          showWarning
        />
      }
    >
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
        {/* Left: Role Card */}
        <section className="flex flex-col">
          <div className="flex items-center gap-2 mb-4">
            <FileText className="w-4 h-4 text-primary" />
            <h2 className="text-sm font-bold text-muted uppercase tracking-widest">Candidate Role Card</h2>
          </div>

          <Card className="flex-1 p-6 space-y-6">
            <div className="border-b border-gray-100 pb-4">
              <p className="text-xs font-bold text-primary uppercase tracking-widest mb-1">OET Speaking Practice</p>
              <h3 className="text-xl font-bold text-navy">{card.title}</h3>
            </div>

            <SpeakingRoleCard
              role={card.profession}
              setting={card.setting}
              patient={card.patient}
              task={card.brief}
              background={card.background}
            />

            <div>
              <p className="text-xs font-bold text-muted uppercase tracking-widest mb-3">Tasks</p>
              <ul className="space-y-3">
                {card.tasks.map((task, i) => (
                  <li key={i} className="flex gap-3 items-start">
                    <span className="shrink-0 w-6 h-6 rounded-full bg-gray-100 text-muted text-xs font-bold flex items-center justify-center">
                      {i + 1}
                    </span>
                    <p className="text-sm text-navy leading-relaxed">{task}</p>
                  </li>
                ))}
              </ul>
            </div>
          </Card>
        </section>

        {/* Right: Prep Tools */}
        <section className="flex flex-col">
          <div className="flex items-center gap-2 mb-4">
            <Edit3 className="w-4 h-4 text-primary" />
            <h2 className="text-sm font-bold text-muted uppercase tracking-widest">Preparation</h2>
          </div>

          <div className="flex-1 flex flex-col gap-6">
            {/* Notes */}
            <Card className="flex-1 flex flex-col overflow-hidden">
              <div className="bg-gray-50 px-4 py-2 border-b border-gray-100 flex items-center justify-between">
                <span className="text-xs font-bold text-muted uppercase">Scratchpad</span>
                <span className="text-xs text-muted">Local only</span>
              </div>
              <textarea
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Jot down your opening, key points, or questions here..."
                className="flex-1 p-4 text-sm text-navy resize-none focus:outline-none leading-relaxed placeholder:text-muted"
              />
            </Card>

            {/* Mode + Start */}
            <Card className="p-6 space-y-6">
              <div className="space-y-3">
                <h4 className="text-xs font-bold text-muted uppercase tracking-widest">Practice Mode</h4>
                <div className="grid grid-cols-3 gap-2">
                  {([
                    { id: 'ai', label: 'AI Patient', icon: Bot, color: 'text-blue-600', bg: 'bg-blue-50' },
                    { id: 'self', label: 'Self-Practice', icon: User, color: 'text-purple-600', bg: 'bg-purple-50' },
                    { id: 'exam', label: 'Simulation', icon: ShieldCheck, color: 'text-amber-600', bg: 'bg-amber-50' },
                  ] as const).map((m) => (
                    <button
                      key={m.id}
                      onClick={() => setSelectedMode(m.id)}
                      className={`flex flex-col items-center gap-2 p-3 rounded-xl border transition-all ${
                        selectedMode === m.id ? 'border-primary bg-primary/5' : 'border-gray-200 hover:border-gray-300'
                      }`}
                    >
                      <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${m.bg}`}>
                        <m.icon className={`w-4 h-4 ${m.color}`} />
                      </div>
                      <span className={`text-xs font-bold ${selectedMode === m.id ? 'text-primary' : 'text-muted'}`}>
                        {m.label}
                      </span>
                    </button>
                  ))}
                </div>
              </div>

              <InlineAlert variant="info">
                {selectedMode === 'ai'
                  ? 'The AI will play the patient role and respond to you.'
                  : selectedMode === 'self'
                    ? 'Record yourself performing the task. No AI feedback during session.'
                    : 'Strict exam conditions. 5-minute timer with no feedback.'}
              </InlineAlert>

              <Button fullWidth size="lg" onClick={handleStartTask}>
                <Play className="w-5 h-5 fill-current" /> Start Speaking Task
              </Button>
            </Card>
          </div>
        </section>
      </div>
    </AppShell>
  );
}
