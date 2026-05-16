'use client';

import { useState, useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  FileText, Play, MessageCircle, User, ShieldCheck, AlertTriangle,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { SpeakingRoleCard } from '@/components/domain/speaking-role-card';
import { Timer } from '@/components/ui/timer';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchRoleCard } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { RoleCard } from '@/lib/mock-data';

type TaskMode = 'self' | 'exam';

export default function RoleCardPreview() {
  const params = useParams();
  const router = useRouter();
  const rawId = params?.id;
  const id = Array.isArray(rawId) ? rawId[0] ?? '' : rawId ?? '';

  const [card, setCard] = useState<RoleCard | null>(null);
  const [loading, setLoading] = useState(true);
  const [notes, setNotes] = useState(() => (
    typeof window === 'undefined' ? '' : window.localStorage.getItem(`speaking-prep:${id}:notes`) ?? ''
  ));
  const [selectedMode, setSelectedMode] = useState<TaskMode>('self');
  const [prepRunning, setPrepRunning] = useState(true);
  const [layLanguagePlan, setLayLanguagePlan] = useState(() => (
    typeof window === 'undefined' ? '' : window.localStorage.getItem(`speaking-prep:${id}:lay-language-plan`) ?? ''
  ));
  const prepTimeSeconds = card?.prepTimeSeconds ?? 180;
  const roleplayTimeSeconds = card?.roleplayTimeSeconds ?? 300;

  useEffect(() => {
    fetchRoleCard(id)
      .then(setCard)
      .catch(() => setCard(null))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    window.localStorage.setItem(`speaking-prep:${id}:notes`, notes);
  }, [id, notes]);

  useEffect(() => {
    window.localStorage.setItem(`speaking-prep:${id}:lay-language-plan`, layLanguagePlan);
  }, [id, layLanguagePlan]);

  const handleStartTask = () => {
    analytics.track('task_started', { taskId: id, subtest: 'speaking', mode: selectedMode });
    router.push(`/speaking/task/${id}?mode=${selectedMode}`);
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Role Card">
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:gap-8">
          <Skeleton className="h-[280px] rounded-xl sm:h-[340px] lg:h-96" />
          <Skeleton className="h-[280px] rounded-xl sm:h-[340px] lg:h-96" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!card) {
    return (
      <LearnerDashboardShell pageTitle="Role Card">
        <InlineAlert variant="error">Role card not found for this task.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell
      pageTitle={card.title}
      navActions={
        <Timer
          mode="countdown"
          initialSeconds={prepTimeSeconds}
          running={prepRunning}
          onComplete={() => setPrepRunning(false)}
          size="md"
          showWarning
        />
      }
    >
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Role Card Preview"
          icon={FileText}
          accent="purple"
          title={card.title}
          description="Use the preparation window to read the card, plan your opening, and choose a practice mode before the recorder starts."
          highlights={[
            { icon: User, label: 'Role', value: card.profession },
            { icon: ShieldCheck, label: 'Prep timer', value: prepRunning ? `${Math.round(prepTimeSeconds / 60)} min running` : 'Finished' },
            { icon: FileText, label: 'Setting', value: card.setting },
          ]}
        />

        <InlineAlert variant="warning">
          {card.disclaimer ?? 'Practice estimate only. This is not an official OET score or result.'}
        </InlineAlert>

        <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
        <section className="flex flex-col">
          <LearnerSurfaceSectionHeader
            eyebrow="Candidate Role Card"
            title="Read the scenario before the timer starts"
            description="Keep the patient, setting, and task bullets visible so your opening sounds organised."
            className="mb-4"
          />

          <Card className="flex-1 p-6 space-y-6">
            <div className="border-b border-border pb-4">
              <p className="text-xs font-bold text-primary uppercase tracking-widest mb-1">OET Speaking Practice</p>
              <h3 className="text-xl font-bold text-navy">{card.title}</h3>
            </div>

            <SpeakingRoleCard
              role={card.profession}
              setting={card.setting}
              patient={card.patient}
              task={card.brief}
              background={card.background}
              tasks={card.tasks}
              patientEmotion={card.patientEmotion}
              communicationGoal={card.communicationGoal}
              clinicalTopic={card.clinicalTopic}
              prepTimeSeconds={prepTimeSeconds}
              roleplayTimeSeconds={roleplayTimeSeconds}
              disclaimer={card.disclaimer}
            />

            {/* Computer-based Speaking paper rule notice (rulebook RULE_61/RULE_75 + RULE_62/RULE_76). */}
            <div className="rounded-2xl border border-warning/30 bg-amber-50 p-4">
              <div className="flex items-start gap-3">
                <AlertTriangle className="h-5 w-5 text-warning shrink-0 mt-0.5" />
                <div className="space-y-1.5 text-sm leading-relaxed">
                  <p className="font-bold text-navy">Computer-based exam rules for this card</p>
                  <p className="text-navy/80">
                    The role-play card above <strong>cannot be annotated on screen</strong>. You are permitted <strong>one blank piece of paper and a pen</strong> to take notes during preparation and during the role play.
                  </p>
                  <p className="text-navy/80">
                    At the end of the exam you <strong>must tear or cut the paper in front of the camera</strong> to confirm nothing leaves the test environment.
                  </p>
                </div>
              </div>
            </div>

            <div>
              <p className="text-xs font-bold text-muted uppercase tracking-widest mb-3">Tasks</p>
              <ul className="space-y-3">
                {card.tasks.map((task, i) => (
                  <li key={i} className="flex gap-3 items-start">
                    <span className="shrink-0 w-6 h-6 rounded-full bg-background-light text-muted text-xs font-bold flex items-center justify-center">
                      {i + 1}
                    </span>
                    <p className="text-sm text-navy leading-relaxed">{task}</p>
                  </li>
                ))}
              </ul>
            </div>

            {(card.warmUpQuestions?.length ?? 0) > 0 && (
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <p className="mb-3 text-xs font-bold uppercase tracking-widest text-muted">Warm-up prompts</p>
                <ul className="space-y-2">
                  {card.warmUpQuestions?.map((question, index) => (
                    <li key={`${question}-${index}`} className="text-sm leading-relaxed text-navy">
                      <span className="font-bold text-primary">{index + 1}.</span> {question}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </Card>
        </section>

        <section className="flex flex-col">
          <LearnerSurfaceSectionHeader
            eyebrow="Preparation"
            title="Plan, then enter the recorder"
            description="Self-practice mode lets you review the transcript afterwards. Simulation mode follows strict exam timing."
            className="mb-4"
          />

          <div className="flex-1 flex flex-col gap-6">
            <Card className="flex-1 flex flex-col overflow-hidden">
              <div className="bg-background-light px-4 py-2 border-b border-border flex items-center justify-between">
                <span className="text-xs font-bold text-muted uppercase">Scratchpad</span>
                <span className="text-xs text-muted">Local only</span>
              </div>
              <textarea
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Jot down your opening, key points, patient concern, and final safety-netting..."
                className="flex-1 p-4 text-sm text-navy resize-none focus:outline-none leading-relaxed placeholder:text-muted"
              />
            </Card>

            <Card className="p-5">
              <label htmlFor="lay-language-plan" className="text-xs font-bold text-muted uppercase tracking-widest">
                Lay-language plan
              </label>
              <p className="mt-1 text-sm leading-relaxed text-muted">
                Convert clinical terms into patient-friendly language before you start.
              </p>
              <textarea
                id="lay-language-plan"
                value={layLanguagePlan}
                onChange={(e) => setLayLanguagePlan(e.target.value)}
                placeholder="Example: 'bronchodilator' -> 'medicine that opens the airways'..."
                className="mt-3 min-h-28 w-full rounded-2xl border border-border bg-background-light p-4 text-sm leading-relaxed text-navy focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
            </Card>

            <Card className="p-6 space-y-6">
              <div className="space-y-3">
                <h4 className="text-xs font-bold text-muted uppercase tracking-widest">Practice Mode</h4>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  {([
                    { id: 'self', label: 'Guided Self-Practice', icon: User, color: 'text-primary', bg: 'bg-primary/10' },
                    { id: 'exam', label: 'Simulation', icon: ShieldCheck, color: 'text-warning', bg: 'bg-amber-50' },
                  ] as const).map((m) => (
                    <button
                      key={m.id}
                      onClick={() => setSelectedMode(m.id)}
                      className={`flex flex-col items-center gap-2 p-3 rounded-xl border transition-all ${
                        selectedMode === m.id ? 'border-primary bg-primary/5' : 'border-border hover:border-border-hover'
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
                {selectedMode === 'self'
                  ? 'Use guided self-practice with local recording and transcript review after the task.'
                  : `Strict exam conditions. ${Math.round(roleplayTimeSeconds / 60)}-minute timer with no feedback and no pause.`}
              </InlineAlert>

              <Button fullWidth size="lg" onClick={handleStartTask}>
                <Play className="w-5 h-5 fill-current" /> Start Speaking Task
              </Button>
            </Card>

            <Card className="border-primary/15 bg-primary/5 p-5">
              <div className="flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-surface text-primary">
                  <MessageCircle className="h-5 w-5" />
                </div>
                <div>
                  <p className="text-sm font-bold text-navy">Need an AI patient?</p>
                  <p className="mt-1 text-sm leading-relaxed text-muted">
                    Interactive AI practice is handled by the dedicated conversation module so it stays server-authoritative.
                  </p>
                  <Link href="/conversation" className="mt-3 inline-flex text-sm font-bold text-primary hover:underline">
                    Open AI Conversation Practice
                  </Link>
                </div>
              </div>
            </Card>
          </div>
        </section>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
