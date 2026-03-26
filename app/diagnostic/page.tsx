'use client';

import { AppShell } from '@/components/layout';
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
} from 'lucide-react';

const SUB_TESTS = [
  {
    name: 'Writing',
    icon: PenLine,
    duration: '45 mins',
    description: 'Compose a clinical letter based on case notes',
    color: 'text-blue-600',
    bg: 'bg-blue-50',
  },
  {
    name: 'Speaking',
    icon: Mic,
    duration: '20 mins',
    description: 'Complete a role play task simulating a clinical scenario',
    color: 'text-violet-600',
    bg: 'bg-violet-50',
  },
  {
    name: 'Reading',
    icon: BookOpen,
    duration: '30 mins',
    description: 'Answer questions on healthcare texts (Parts A, B, and C)',
    color: 'text-emerald-600',
    bg: 'bg-emerald-50',
  },
  {
    name: 'Listening',
    icon: Headphones,
    duration: '25 mins',
    description: 'Listen to clinical consultations and answer questions',
    color: 'text-amber-600',
    bg: 'bg-amber-50',
  },
];

export default function DiagnosticIntroPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [starting, setStarting] = useState(false);
  const [overview, setOverview] = useState<any>(null);
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

    return () => { cancelled = true; };
  }, []);

  const subTests = useMemo(() => {
    const serverSubtests = overview?.subtests ?? [];
    if (serverSubtests.length === 0) return SUB_TESTS;

    return SUB_TESTS.map((sub) => {
      const match = serverSubtests.find((item: any) => String(item.subtest).toLowerCase() === sub.name.toLowerCase());
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
    <AppShell pageTitle="Diagnostic Assessment">
      <div className="max-w-3xl mx-auto px-4 sm:px-6 py-8 space-y-8">
        {/* Trust notice */}
        <InlineAlert variant="info" className="border-primary/20 bg-primary/5">
          <div className="flex items-start gap-3">
            <ShieldCheck className="w-5 h-5 text-primary shrink-0 mt-0.5" />
            <div>
              <p className="font-bold text-navy text-sm">Training Estimate Only</p>
              <p className="text-xs text-muted mt-1">
                This diagnostic gives you an AI-estimated score range to personalise your study plan.
                It is <strong>not</strong> an official OET score. Use it as a guide to identify strengths and areas for improvement.
              </p>
            </div>
          </div>
        </InlineAlert>

         {error && <InlineAlert variant="error">{error}</InlineAlert>}

         {/* Hero section */}
         <div className="text-center space-y-3">
          <h1 className="text-2xl sm:text-3xl font-bold text-navy">
            Let&apos;s Find Your Starting Point
          </h1>
          <p className="text-sm text-muted max-w-lg mx-auto">
            Complete a short diagnostic across all four OET sub-tests. The results will be used to
            create a personalised study plan tailored to your strengths and weaknesses.
          </p>
          <div className="flex items-center justify-center gap-2 text-sm font-semibold text-navy">
            <Clock className="w-4 h-4 text-primary" />
            Estimated total time: {totalDuration}
          </div>
        </div>

        {/* Sub-test cards */}
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

        {/* Key info */}
        <Card className="bg-gray-50/50 border-gray-200">
          <h3 className="text-sm font-bold text-navy mb-3">Before You Begin</h3>
          <ul className="space-y-2 text-xs text-muted">
            <li className="flex items-start gap-2">
              <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">1</span>
              You can complete sub-tests in <strong className="text-navy">any order</strong> and take breaks between them.
            </li>
            <li className="flex items-start gap-2">
              <span className="w-5 h-5 rounded-full bg-primary/10 text-primary text-xs font-bold flex items-center justify-center shrink-0 mt-px">2</span>
              Your progress is <strong className="text-navy">saved automatically</strong> — you can resume later.
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

        {/* Start CTA */}
        <div className="sticky bottom-4 z-10 flex justify-center">
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
    </AppShell>
  );
}
