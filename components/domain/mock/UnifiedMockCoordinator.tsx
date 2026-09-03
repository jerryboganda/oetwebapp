'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  AlertCircle,
  ArrowRight,
  BookOpen,
  CheckCircle2,
  Clock,
  ExternalLink,
  FileText,
  Headphones,
  Mic,
  PenTool,
  Play,
  RotateCcw,
  Shield,
  Timer,
  Zap,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { cn } from '@/lib/utils';
import type { MockSession, MockSessionSection } from '@/lib/mock-data';
import { completeMockSection, startMockSection, submitMockSession } from '@/lib/api';
import { correctedNowMs, readServerClockOffsetMs } from '@/lib/server-clock';

export interface UnifiedMockCoordinatorProps {
  session: MockSession;
  onRefreshSession?: () => Promise<MockSession | null>;
  className?: string;
}

export type SubtestType = 'listening' | 'reading' | 'writing' | 'speaking';

const SUBTEST_META: Record<
  SubtestType,
  {
    title: string;
    icon: typeof Headphones;
    durationMinutes: number;
    description: string;
    rules: string[];
    accent: string;
  }
> = {
  listening: {
    title: 'Listening Sub-Test',
    icon: Headphones,
    durationMinutes: 45,
    description: '42 questions across 3 parts (A, B, C). Audio plays once only.',
    rules: [
      'Part A: 2 consultations, note completion (24 questions)',
      'Part B: 6 workplace extracts, multiple choice (6 questions)',
      'Part C: 2 presentations/interviews, multiple choice (12 questions)',
      'One-play audio: pause and scrub seeking are blocked in exam mode',
    ],
    accent: 'emerald',
  },
  reading: {
    title: 'Reading Sub-Test',
    icon: BookOpen,
    durationMinutes: 60,
    description: '42 questions across 3 parts. Part A is strictly timed at 15 minutes.',
    rules: [
      'Part A: 20 questions expeditious reading (strict 15-min lock)',
      'Part B: 6 workplace extracts (1 question each)',
      'Part C: 16 questions deep reading (2 texts x 8 MCQs)',
      'Option strikethrough and passage annotation tools available',
    ],
    accent: 'sky',
  },
  writing: {
    title: 'Writing Sub-Test',
    icon: PenTool,
    durationMinutes: 45,
    description: 'Formal clinical letter (referral, transfer, or discharge).',
    rules: [
      '5-minute reading window with locked editor for case note review',
      '40-minute writing phase with real-time 180–200 word count tracking',
      'Evaluated across 6 official OET criteria (Purpose, Content, Language, etc.)',
      'Hard auto-submission on 45-minute mark',
    ],
    accent: 'amber',
  },
  speaking: {
    title: 'Speaking Sub-Test',
    icon: Mic,
    durationMinutes: 20,
    description: '2 clinical role-play cards with an AI patient avatar.',
    rules: [
      'Short unscored warm-up identification',
      'Role-play Card 1: 3-minute preparation + 5-minute consultation',
      'Role-play Card 2: 3-minute preparation + 5-minute consultation',
      'Evaluated across 9 criteria (4 linguistic + 5 clinical communication)',
    ],
    accent: 'violet',
  },
};

export function UnifiedMockCoordinator({
  session,
  onRefreshSession,
  className,
}: UnifiedMockCoordinatorProps) {
  const router = useRouter();
  const [transitionSection, setTransitionSection] = useState<MockSessionSection | null>(null);
  const [transitionOpen, setTransitionOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionSuccess, setActionSuccess] = useState<string | null>(null);

  // Server clock offset for unified drift monitoring
  const [clockDriftMs, setClockDriftMs] = useState<number>(0);
  useEffect(() => {
    setClockDriftMs(readServerClockOffsetMs());
  }, []);

  const totalSections = session.sectionStates.length;
  const completedSections = useMemo(
    () => session.sectionStates.filter((s) => s.status === 'completed'),
    [session.sectionStates],
  );
  const isAllCompleted = completedSections.length === totalSections && totalSections > 0;

  // Active section or next runnable section
  const nextSection = useMemo(() => {
    return session.sectionStates.find((s) => s.status === 'in_progress')
      ?? session.sectionStates.find((s) => s.status === 'not_started')
      ?? null;
  }, [session.sectionStates]);

  const handleLaunchSection = useCallback(
    async (section: MockSessionSection) => {
      setActionError(null);
      try {
        const started = await startMockSection(session.sessionId, section.id, {
          preflight: 'passed',
        });
        if (onRefreshSession) await onRefreshSession();
        router.push(started.launchRoute);
      } catch (err) {
        setActionError(err instanceof Error ? err.message : 'Could not launch section.');
      }
    },
    [session.sessionId, onRefreshSession, router],
  );

  const handleOpenTransitionModal = (section: MockSessionSection) => {
    setTransitionSection(section);
    setTransitionOpen(true);
  };

  const handleConfirmTransition = async () => {
    if (!transitionSection) return;
    setTransitionOpen(false);
    await handleLaunchSection(transitionSection);
  };

  const handleSubmitAll = async () => {
    setSubmitting(true);
    setActionError(null);
    setActionSuccess(null);
    try {
      await submitMockSession(session.sessionId);
      setActionSuccess('Full 4-skill mock submitted successfully! Generating official report…');
      if (onRefreshSession) await onRefreshSession();
      router.push(`/mocks/report/${session.sessionId}`);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Could not submit full mock session.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className={cn('space-y-6', className)}>
      {/* Top Banner: Unified Session Header & Governance */}
      <Card className="border-border bg-surface p-6 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
                <Timer className="h-5 w-5" />
              </span>
              <h2 className="text-xl font-bold text-navy">4-Skill Unified Mock Orchestrator</h2>
            </div>
            <p className="text-sm text-muted">
              Standard OET Simulation Sequence: Listening → Reading → Writing → Speaking
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Badge variant="outline" className="flex items-center gap-1.5 px-3 py-1 font-mono text-xs">
              <Shield className="h-3.5 w-3.5 text-emerald-600" />
              Clock Sync: {Math.abs(clockDriftMs) < 1000 ? 'Synchronized' : `${clockDriftMs}ms offset`}
            </Badge>
            <Badge
              variant={session.config.mode === 'exam' ? 'default' : 'outline'}
              className="px-3 py-1 text-xs uppercase"
            >
              {session.config.mode === 'exam' ? 'Strict Exam Mode' : 'Practice Mode'}
            </Badge>
            <Badge variant="outline" className="px-3 py-1 font-mono text-xs">
              {completedSections.length} / {totalSections} Sub-Tests Complete
            </Badge>
          </div>
        </div>

        {/* Global Progress Bar */}
        <div className="mt-5 space-y-1.5">
          <div className="flex justify-between text-xs font-semibold text-muted">
            <span>Overall Simulation Progress</span>
            <span>{Math.round((completedSections.length / Math.max(1, totalSections)) * 100)}%</span>
          </div>
          <div className="h-2 w-full overflow-hidden rounded-full bg-background-light">
            <div
              className="h-full rounded-full bg-primary transition-all duration-300"
              style={{
                width: `${(completedSections.length / Math.max(1, totalSections)) * 100}%`,
              }}
            />
          </div>
        </div>
      </Card>

      {actionError ? <InlineAlert variant="error">{actionError}</InlineAlert> : null}
      {actionSuccess ? <InlineAlert variant="success">{actionSuccess}</InlineAlert> : null}

      {/* Subtest Stepper Pipeline */}
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {session.sectionStates.map((section, idx) => {
          const subtestKey = (section.subtest?.toLowerCase() ?? 'reading') as SubtestType;
          const meta = SUBTEST_META[subtestKey] ?? SUBTEST_META.reading;
          const Icon = meta.icon;
          const isDone = section.status === 'completed';
          const isInProgress = section.status === 'in_progress';
          const isNotStarted = section.status === 'not_started';

          return (
            <Card
              key={section.id}
              className={cn(
                'relative flex flex-col justify-between p-5 transition-all',
                isInProgress && 'ring-2 ring-primary border-primary bg-primary/5',
                isDone && 'border-emerald-200 bg-emerald-50/20',
              )}
            >
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-surface border border-border text-foreground shadow-xs">
                    <Icon className="h-5 w-5 text-primary" />
                  </span>
                  <Badge
                    variant={isDone ? 'success' : isInProgress ? 'info' : 'outline'}
                    className={cn(
                      'text-[11px] font-semibold uppercase',
                      isDone && 'bg-emerald-600 text-white',
                    )}
                  >
                    {isDone ? 'Completed' : isInProgress ? 'In Progress' : 'Not Started'}
                  </Badge>
                </div>

                <div>
                  <span className="text-[11px] font-bold uppercase tracking-wider text-muted">
                    Sub-Test {idx + 1}
                  </span>
                  <h3 className="text-base font-bold text-navy">{section.title || meta.title}</h3>
                  <p className="mt-1 text-xs text-muted leading-relaxed line-clamp-2">
                    {meta.description}
                  </p>
                </div>

                <div className="flex items-center gap-2 text-xs text-muted">
                  <Clock className="h-3.5 w-3.5" />
                  <span>{meta.durationMinutes} minutes allocation</span>
                </div>
              </div>

              <div className="mt-5 pt-3 border-t border-border/60">
                {isDone ? (
                  <div className="flex items-center justify-between">
                    <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-emerald-700">
                      <CheckCircle2 className="h-4 w-4" /> Ready for scoring
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleLaunchSection(section)}
                      className="text-xs"
                    >
                      Review
                    </Button>
                  </div>
                ) : isInProgress ? (
                  <Button
                    variant="primary"
                    size="sm"
                    className="w-full gap-2 font-bold"
                    onClick={() => handleLaunchSection(section)}
                  >
                    <Play className="h-3.5 w-3.5 fill-current" /> Resume Now
                  </Button>
                ) : (
                  <Button
                    variant="secondary"
                    size="sm"
                    className="w-full gap-1.5 text-xs font-semibold"
                    onClick={() => handleOpenTransitionModal(section)}
                  >
                    Start Sub-Test <ArrowRight className="h-3.5 w-3.5" />
                  </Button>
                )}
              </div>
            </Card>
          );
        })}
      </div>

      {/* Completion Actions */}
      {isAllCompleted ? (
        <Card className="border-emerald-300 bg-emerald-50/50 p-6 text-center shadow-sm">
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
            <CheckCircle2 className="h-7 w-7" />
          </div>
          <h3 className="mt-3 text-lg font-bold text-navy">All 4 Sub-Tests Completed</h3>
          <p className="mx-auto mt-1 max-w-md text-sm text-muted">
            Your responses for Reading, Listening, Writing, and Speaking are recorded. Submit now to
            aggregate your Statement of Results and official band scores.
          </p>
          <div className="mt-5 flex flex-wrap justify-center gap-3">
            <Button
              variant="primary"
              size="lg"
              loading={submitting}
              disabled={submitting}
              onClick={handleSubmitAll}
              className="font-bold shadow-md"
            >
              <Zap className="mr-2 h-4 w-4" /> Generate Official Statement of Results
            </Button>
            <Button
              variant="outline"
              size="lg"
              onClick={() => router.push(`/mocks/report/${session.sessionId}`)}
            >
              <ExternalLink className="mr-2 h-4 w-4" /> View Existing Report
            </Button>
          </div>
        </Card>
      ) : nextSection ? (
        <div className="flex items-center justify-between rounded-xl border border-primary/20 bg-primary/5 p-4">
          <div className="flex items-center gap-3">
            <Clock className="h-5 w-5 text-primary" />
            <div>
              <p className="text-sm font-bold text-navy">Ready for next sub-test</p>
              <p className="text-xs text-muted">Next up: {nextSection.title}</p>
            </div>
          </div>
          <Button
            variant="primary"
            size="sm"
            onClick={() => handleOpenTransitionModal(nextSection)}
            className="gap-2 font-bold"
          >
            Continue Simulation <ArrowRight className="h-4 w-4" />
          </Button>
        </div>
      ) : null}

      {/* Sub-Test Transition Briefing Modal */}
      {transitionSection ? (
        <Modal
          open={transitionOpen}
          onClose={() => setTransitionOpen(false)}
          title={`Prepare for ${transitionSection.title}`}
        >
          {(() => {
            const subtestKey = (transitionSection.subtest?.toLowerCase() ?? 'reading') as SubtestType;
            const meta = SUBTEST_META[subtestKey] ?? SUBTEST_META.reading;
            const Icon = meta.icon;

            return (
              <div className="space-y-4 py-2">
                <div className="flex items-start gap-3 rounded-xl border border-border bg-surface p-4">
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
                    <Icon className="h-6 w-6" />
                  </span>
                  <div>
                    <h4 className="font-bold text-navy">{meta.title}</h4>
                    <p className="text-xs text-muted mt-0.5">{meta.description}</p>
                    <Badge variant="outline" className="mt-2 text-[11px]">
                      Duration: {meta.durationMinutes} Minutes
                    </Badge>
                  </div>
                </div>

                <div className="space-y-2 rounded-xl border border-border bg-background-light p-4">
                  <p className="text-xs font-bold uppercase tracking-wider text-muted">
                    Sub-Test Protocol & Invariants
                  </p>
                  <ul className="list-disc space-y-1.5 pl-4 text-xs text-foreground">
                    {meta.rules.map((rule, rIdx) => (
                      <li key={rIdx}>{rule}</li>
                    ))}
                  </ul>
                </div>

                <div className="flex items-center justify-end gap-2 pt-2">
                  <Button variant="ghost" onClick={() => setTransitionOpen(false)}>
                    Cancel
                  </Button>
                  <Button variant="primary" onClick={handleConfirmTransition} className="gap-2">
                    <Play className="h-4 w-4 fill-current" /> Begin Sub-Test Now
                  </Button>
                </div>
              </div>
            );
          })()}
        </Modal>
      ) : null}
    </div>
  );
}
