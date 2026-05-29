'use client';

// Phase 9 of LISTENING-MODULE-PLAN.md
// ─────────────────────────────────────────────────────────────────────────────
// Pre-flight Listening test-rules lesson. This is a learner-facing briefing
// that loads before someone enters paper-mode (or any first attempt at a real
// Listening mock) so they go in knowing exactly how the OET Listening sub-
// test works: one play, no negative marking, MCQ + gap-fill item types, exam-
// integrity rules, and recommended in-test strategy.
//
// The numeric constants (42 questions / 40 min / 30-raw pass / 350-scaled
// pass) are fetched from the anonymous-allowed policy endpoint so spec
// changes don't require a code deploy. The page falls back to baked-in
// defaults if the fetch fails.
//
// Routing: linked from the Listening home page CTA and from the player
// pre-roll. Anonymous-allowed (no learner data is fetched here).

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, CheckCircle2, Headphones, Pencil, ShieldCheck, Timer, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { MotionItem } from '@/components/ui/motion-primitives';
import { getListeningTestRulesPolicy, type ListeningTestRulesPolicyDto } from '@/lib/listening-api';

const DEFAULT_RULES: ListeningTestRulesPolicyDto = {
  questionCount: 42,
  durationMinutes: 40,
  partA: { items: 24, extracts: 2, itemType: 'short-answer' },
  partB: { items: 6, extracts: 6, itemType: 'mcq-3-option' },
  partC: { items: 12, extracts: 2, itemType: 'mcq-3-option' },
  passRawAnchor: 30,
  passScaledAnchor: 350,
  scaledMax: 500,
};

function numberWord(n: number): string {
  const words: Record<number, string> = {
    40: 'Forty', 41: 'Forty-one', 42: 'Forty-two', 43: 'Forty-three',
    44: 'Forty-four', 45: 'Forty-five', 50: 'Fifty', 60: 'Sixty',
  };
  return words[n] ?? n.toString();
}

export default function ListeningTestRulesPage() {
  const [rules, setRules] = useState<ListeningTestRulesPolicyDto>(DEFAULT_RULES);

  useEffect(() => {
    let cancelled = false;
    getListeningTestRulesPolicy()
      .then((data) => { if (!cancelled) setRules(data); })
      .catch(() => { /* swallow — DEFAULT_RULES already rendered */ });
    return () => { cancelled = true; };
  }, []);

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
            title={`One play. ${numberWord(rules.questionCount)} questions. ~${rules.durationMinutes} minutes.`}
            points={[
              'You hear the audio ONCE. There is no rewind in paper mode.',
              `Three parts: A (${rules.partA.items} short-answers, ${rules.partA.extracts} consultations), B (${rules.partB.items} MCQs, workplace extracts), C (${rules.partC.items} MCQs, ${rules.partC.extracts} presentations).`,
              'You write answers on the question paper as you listen, then transfer them at the end (in real OET).',
            ]}
          />
          <RuleCard
            icon={CheckCircle2}
            title="No negative marking"
            points={[
              'A wrong answer scores zero. A blank answer scores zero. Always write something.',
              `${rules.passRawAnchor}/${rules.questionCount} raw ≡ ${rules.passScaledAnchor}/${rules.scaledMax} scaled. Pass = ${rules.passScaledAnchor}.`,
              'Spelling that does not change the meaning is accepted (e.g. "discharge" / "dischare" both pass; "discharge" vs "discarded" do not).',
            ]}
          />
          <RuleCard
            icon={Pencil}
            title={`Part A gap-fill (${rules.partA.items} items)`}
            points={[
              'Listen for the exact word the speaker says. Re-using the words from the gap stem is the safest bet.',
              'Numbers, dosages, dates, and units count exactly: "5 mg" ≠ "5 g".',
              'Plural / singular and articles (a / the) only matter when they change meaning.',
            ]}
          />
          <RuleCard
            icon={Volume2}
            title="Parts B & C: MCQs (3-option)"
            points={[
              'Distractors are designed to sound like the right answer. Watch for "too strong / too weak", opposite meaning, wrong speaker, or a re-used keyword.',
              'In Part C, listen for the speaker\'s attitude (concerned / optimistic / doubtful / critical / neutral).',
              'Read the stem and options before the audio starts. You have a short reading window.',
            ]}
          />
          <RuleCard
            icon={ShieldCheck}
            title="Exam integrity (paper mode)"
            points={[
              'Once you enter paper mode the audio plays end-to-end and answers cannot be revised after submit.',
              'Headphones recommended. Do not switch tabs; paper mode flags loss-of-focus.',
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
              'Use the post-attempt review to study why each distractor was wrong. That is where the score gain lives.',
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
            className="inline-flex items-center gap-1 text-sm font-medium text-primary transition-colors hover:text-primary-dark"
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
  const accent = tone === 'amber' ? 'text-amber-600' : 'text-primary';
  return (
    <MotionItem>
      <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
        <div className="flex items-start gap-3">
          <Icon className={`h-5 w-5 shrink-0 ${accent}`} aria-hidden />
          <h2 className="text-lg font-semibold text-navy">{title}</h2>
        </div>
        <ul className="mt-4 space-y-2 text-sm text-muted">
          {points.map((point, i) => (
            <li key={i} className="flex gap-2">
              <span className="mt-1.5 inline-block h-1.5 w-1.5 shrink-0 rounded-full bg-primary/40" aria-hidden />
              <span>{point}</span>
            </li>
          ))}
        </ul>
      </div>
    </MotionItem>
  );
}
