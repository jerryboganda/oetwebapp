'use client';

// Phase 9 of LISTENING-MODULE-PLAN.md
// ─────────────────────────────────────────────────────────────────────────────
// Pre-flight Listening test-rules lesson. This is a static, learner-facing
// briefing that loads before someone enters paper-mode (or any first attempt
// at a real Listening mock) so they go in knowing exactly how the OET
// Listening sub-test works: one play, no negative marking, MCQ + gap-fill
// item types, exam-integrity rules, and recommended in-test strategy.
//
// Routing: linked from the Listening home page CTA and from the player
// pre-roll. Anonymous-allowed (no learner data is fetched here).

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Headphones, Pencil, ShieldCheck, Timer, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { MotionItem } from '@/components/ui/motion-primitives';

export default function ListeningTestRulesPage() {
  return (
    <LearnerDashboardShell>
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Listening Test Rules"
          title="How the OET Listening sub-test works"
          description="A two-minute brief covering exam timing, item types, and the rules you must follow in paper mode. Read this before your first real attempt."
        />

        <div className="grid gap-6 lg:grid-cols-2">
          <RuleCard
            icon={Timer}
            title="One play. Forty-two questions. ~40 minutes."
            points={[
              'You hear the audio ONCE. There is no rewind in paper mode.',
              'Three parts: A (24 short-answers, 2 consultations), B (6 MCQs, workplace extracts), C (12 MCQs, 2 presentations).',
              'You write answers on the question paper as you listen, then transfer them at the end (in real OET).',
            ]}
          />
          <RuleCard
            icon={CheckCircle2}
            title="No negative marking"
            points={[
              'A wrong answer scores zero. A blank answer scores zero. Always write something.',
              '30/42 raw ≡ 350/500 scaled. Pass = 350.',
              'Spelling that does not change the meaning is accepted (e.g. "discharge" / "dischare" both pass; "discharge" vs "discarded" do not).',
            ]}
          />
          <RuleCard
            icon={Pencil}
            title="Part A — gap-fill (24 items)"
            points={[
              'Listen for the exact word the speaker says. Re-using the words from the gap stem is the safest bet.',
              'Numbers, dosages, dates, and units count exactly: "5 mg" ≠ "5 g".',
              'Plural / singular and articles (a / the) only matter when they change meaning.',
            ]}
          />
          <RuleCard
            icon={Volume2}
            title="Parts B & C — MCQs (3-option)"
            points={[
              'Distractors are designed to sound like the right answer — watch for "too strong / too weak", opposite meaning, wrong speaker, or a re-used keyword.',
              'In Part C, listen for the speaker\'s attitude (concerned / optimistic / doubtful / critical / neutral).',
              'Read the stem and options before the audio starts — you have a short reading window.',
            ]}
          />
          <RuleCard
            icon={ShieldCheck}
            title="Exam integrity (paper mode)"
            points={[
              'Once you enter paper mode the audio plays end-to-end and answers cannot be revised after submit.',
              'Headphones recommended. Do not switch tabs — paper mode flags loss-of-focus.',
              'Transcripts are NOT shown during the attempt. They unlock per-item in your post-attempt review.',
            ]}
            tone="amber"
          />
          <RuleCard
            icon={Headphones}
            title="Strategy that actually works"
            points={[
              'Predict the answer type before you listen (number? job title? medication?).',
              'If you miss an item, write your best guess and move on. The audio will not wait.',
              'Use the post-attempt review to study why each distractor was wrong — that is where the score gain lives.',
            ]}
          />
        </div>

        <MotionItem>
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'indigo',
              eyebrow: 'Ready?',
              title: 'Start your Listening attempt',
              description: 'Choose home mode for relaxed practice with replay, or paper mode to simulate the real exam (one-play, no replay).',
              primaryAction: { label: 'Open Listening Home', href: '/listening' },
              secondaryAction: { label: 'View Mocks', href: '/mocks' },
            }}
          />
        </MotionItem>

        <div className="flex items-center justify-between">
          <Badge variant="muted">Static briefing · no data collected on this page</Badge>
          <Link
            href="/listening"
            className="inline-flex items-center gap-1 text-sm font-medium text-indigo-600 hover:text-indigo-500"
          >
            Back to Listening <ArrowRight className="ml-1 h-4 w-4" aria-hidden />
          </Link>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}

function RuleCard({
  icon: Icon,
  title,
  points,
  tone,
}: {
  icon: React.ComponentType<{ className?: string }>;
  title: string;
  points: string[];
  tone?: 'amber';
}) {
  const accent = tone === 'amber' ? 'text-amber-600' : 'text-indigo-600';
  return (
    <MotionItem>
      <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <div className="flex items-start gap-3">
          <div className={`rounded-lg bg-slate-100 p-2 dark:bg-slate-800 ${accent}`}>
            <Icon className="h-5 w-5" aria-hidden />
          </div>
          <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{title}</h2>
        </div>
        <ul className="mt-4 space-y-2 text-sm text-slate-700 dark:text-slate-300">
          {points.map((point, i) => (
            <li key={i} className="flex gap-2">
              <span className="mt-1 inline-block h-1.5 w-1.5 shrink-0 rounded-full bg-slate-400" aria-hidden />
              <span>{point}</span>
            </li>
          ))}
        </ul>
      </div>
    </MotionItem>
  );
}
