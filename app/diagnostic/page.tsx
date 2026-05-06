'use client';

import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { useAnalytics } from '@/hooks/use-analytics';
import { useRouter } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';
import { fetchDiagnosticOverview, startDiagnostic } from '@/lib/api';
import {
  PenLine,
  Mic,
  BookOpen,
  Headphones,
  Clock,
  ArrowRight,
  ShieldCheck,
  Activity,
} from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { MotionSection } from '@/components/ui/motion-primitives';

const SUB_TESTS = [
  {
    name: 'Writing',
    icon: PenLine,
    duration: '45 mins',
    description: 'Complete a timed writing task aligned to your selected exam family',
    color: 'text-info',
    bg: 'bg-info/10',
  },
  {
    name: 'Speaking',
    icon: Mic,
    duration: '20 mins',
    description: 'Respond to a timed speaking prompt aligned to your selected exam family',
    color: 'text-primary',
    bg: 'bg-primary/10',
  },
  {
    name: 'Reading',
    icon: BookOpen,
    duration: '30 mins',
    description: 'Answer questions on exam-style reading passages',
    color: 'text-success',
    bg: 'bg-success/10',
  },
  {
    name: 'Listening',
    icon: Headphones,
    duration: '25 mins',
    description: 'Listen to exam-style audio and answer questions',
    color: 'text-warning',
    bg: 'bg-amber-50',
  },
];

export default function DiagnosticIntroPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [starting, setStarting] = useState(false);
  const [overview, setOverview] = useState<{ subtests?: Array<{ subtest: string; estimatedDurationMinutes: number }>; estimatedTotalMinutes?: number; disclaimer?: string } | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const data = await fetchDiagnosticOverview();
        if (!cancelled) {
          setOverview(data);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load diagnostic overview.');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const subTests = useMemo(() => {
    const serverSubtests = overview?.subtests ?? [];
    if (serverSubtests.length === 0) return SUB_TESTS;

    return SUB_TESTS.map((sub) => {
      const match = serverSubtests.find((item) => String(item.subtest).toLowerCase() === sub.name.toLowerCase());
      return {
        ...sub,
        duration: match ? `${match.estimatedDurationMinutes} mins` : sub.duration,
      };
    });
  }, [overview]);

  const totalDuration = useMemo(() => {
    if (!overview?.estimatedTotalMinutes) return '~2 hours';
    const hours = overview.estimatedTotalMinutes / 60;
    return hours >= 2 ? `~${Math.round(hours)} hours` : `~${overview.estimatedTotalMinutes} mins`;
  }, [overview]);

  const diagnosticDisclaimer = overview?.disclaimer ?? 'Diagnostic results are training estimates only and are not official exam scores.';

  const handleStart = async () => {
    setStarting(true);
    try {
      await startDiagnostic();
      track('diagnostic_started', { trigger: 'intro_page' });
      router.push('/diagnostic/hub');
    } finally {
      setStarting(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Diagnostic Assessment">
      <div className="space-y-8">
        <InlineAlert variant="info" className="border-primary/20 bg-primary/5">
          <div className="flex items-start gap-3">
            <ShieldCheck className="w-5 h-5 text-primary shrink-0 mt-0.5" />
            <div>
              <p className="font-bold text-navy text-sm">Training Estimate Only</p>
              <p className="text-xs text-muted mt-1">
                {diagnosticDisclaimer} Use it as a guide to identify strengths and areas for improvement.
              </p>
            </div>
          </div>
        </InlineAlert>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <MotionSection delayIndex={0}>
        <LearnerPageHero
          eyebrow="Starting Point"
          icon={Activity}
          accent="primary"
          title="Build your baseline before the study plan starts"
          description="Use the diagnostic to map your current level across all four sub-tests before the app starts prioritising practice."
          highlights={[
            { icon: Clock, label: 'Estimated time', value: totalDuration },
            { icon: Activity, label: 'Sub-tests', value: `${subTests.length} included` },
            { icon: ShieldCheck, label: 'Result type', value: 'Practice estimate' },
          ]}
        />
        </MotionSection>

        <LearnerSkillSwitcher compact />

        <MotionSection delayIndex={1}>
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Diagnostic Scope"
            title="See exactly what the diagnostic covers"
            description="Each sub-test card explains the task type and timing before the learner begins."
            className="mb-4"
          />
          <div className="grid gap-4 sm:grid-cols-2">
            {subTests.map((sub) => {
              const Icon = sub.icon;
              return (
                <Card key={sub.name} className="flex items-start gap-4">
                  <div
                    className={`w-12 h-12 rounded-xl ${sub.bg} ${sub.color} flex items-center justify-center shrink-0`}
                  >
                    <Icon className="w-6 h-6" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-bold text-navy">{sub.name}</p>
                    <p className="text-xs text-muted mt-0.5">{sub.description}</p>
                    <p className="text-xs font-semibold text-navy mt-1 flex items-center gap-1">
                      <Clock className="w-3 h-3 text-muted" />
                      {sub.duration}
                    </p>
                  </div>
                </Card>
              );
            })}
          </div>
        </section>
        </MotionSection>

        <MotionSection delayIndex={2}>
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Before You Begin"
            title="Know the flow before you commit"
            description="This page should answer what the diagnostic is, how it behaves, and what the learner gets afterward."
            className="mb-4"
          />
          <Card className="bg-background-light/50 border-border">
            <ul className="space-y-2 text-xs text-muted">
              <li className="flex items-start gap-2">
                <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">1</span>
                You can complete sub-tests in <strong className="text-navy">any order</strong> and take breaks between them.
              </li>
              <li className="flex items-start gap-2">
                <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">2</span>
                Your progress is <strong className="text-navy">saved automatically</strong> so you can resume later.
              </li>
              <li className="flex items-start gap-2">
                <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">3</span>
                For the Speaking task, you&apos;ll need <strong className="text-navy">microphone access</strong> in a quiet environment.
              </li>
              <li className="flex items-start gap-2">
                <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">4</span>
                Results are generated by AI and will be ready when you complete all sections.
              </li>
            </ul>
          </Card>
        </section>
        </MotionSection>

        <div className="sticky bottom-[calc(var(--bottom-nav-height)+0.5rem)] lg:bottom-6 z-10 flex justify-center pb-2">
          <Button
            size="lg"
            loading={starting}
            onClick={handleStart}
            className="shadow-lg gap-2"
          >
            Begin Diagnostic
            <ArrowRight className="w-4 h-4" />
          </Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
