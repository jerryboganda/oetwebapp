'use client';

import { useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  ChevronRight,
  ClipboardCheck,
  Coins,
  Gift,
  Headphones,
  Mic,
  PenLine,
  ShieldCheck,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { Modal } from '@/components/ui/modal';

// A single, friendly "How credits work" button that opens a popup explaining the
// whole credit system across all five surfaces (Reading, Listening, Writing,
// Speaking, Mocks). Drop <CreditsGuideButton /> anywhere a learner is about to
// spend credits. The numbers here mirror the live billing rules (FINAL 2026-09-06):
//   Reading / Listening — 1 credit per PAPER (parts + re-tries free)
//   Writing             — 2 credits per letter (no parts)
//   Speaking            — 2 credits per card; full two-card exam = 4 credits
//   Mock                — 1 mock credit; Writing/Speaking are AI-graded inside it (no AI credits)

type Accent = 'blue' | 'violet' | 'amber' | 'emerald' | 'rose';

const ACCENT: Record<Accent, { medallion: string; pill: string; ring: string }> = {
  blue: {
    medallion: 'bg-info/10 text-info',
    pill: 'bg-info/10 text-info',
    ring: 'border-info/20',
  },
  violet: {
    medallion: 'bg-lavender text-primary-dark',
    pill: 'bg-lavender text-primary-dark',
    ring: 'border-primary/20',
  },
  amber: {
    medallion: 'bg-warning/10 text-warning-strong',
    pill: 'bg-warning/10 text-warning-strong',
    ring: 'border-warning/20',
  },
  emerald: {
    medallion: 'bg-success/10 text-success-strong',
    pill: 'bg-success/10 text-success-strong',
    ring: 'border-success/20',
  },
  rose: {
    medallion: 'bg-danger/10 text-danger-strong',
    pill: 'bg-danger/10 text-danger-strong',
    ring: 'border-danger/20',
  },
};

interface CreditRow {
  icon: LucideIcon;
  accent: Accent;
  name: string;
  cost: string;
  detail: string;
}

const ROWS: CreditRow[] = [
  {
    icon: BookOpen,
    accent: 'blue',
    name: 'Reading',
    cost: '1 credit',
    detail:
      'Charged per paper. Open any part (A, B or C) or the full paper and the whole sample unlocks — the other parts and any re-tries are free. A different sample is a new credit.',
  },
  {
    icon: Headphones,
    accent: 'violet',
    name: 'Listening',
    cost: '1 credit',
    detail:
      'Just like Reading: one credit unlocks the whole paper — every part and re-try of that sample is then free.',
  },
  {
    icon: PenLine,
    accent: 'amber',
    name: 'Writing',
    cost: '2 credits',
    detail:
      'Charged per exam — one AI-marked letter. Writing has no parts. Packages are sold in letters but shown in credits: 6 Writing credits = 3 letters.',
  },
  {
    icon: Mic,
    accent: 'emerald',
    name: 'Speaking',
    cost: '2 credits',
    detail:
      'A single Speaking card uses 2 AI credits (6 Speaking credits = 3 cards). A full Speaking exam contains two cards and uses 4 AI credits in total.',
  },
  {
    icon: ClipboardCheck,
    accent: 'rose',
    name: 'Mock exam',
    cost: '1 mock credit',
    detail:
      'A full mock uses one Mock credit, a separate allowance. Writing & Speaking in a mock are AI-graded within that mock credit, so they don’t use your AI credits.',
  },
  {
    icon: Gift,
    accent: 'violet',
    name: 'Free sample',
    cost: 'No credits',
    detail:
      'Look for the FREE SAMPLE card: one free Listening paper, one free Reading paper, plus one AI-graded Writing letter and one AI-graded Speaking card. Free samples never use your credits.',
  },
];

export function CreditsGuideButton({
  className = '',
  label = 'How credits work',
  variant = 'pill',
}: {
  className?: string;
  label?: string;
  /** `pill` is the compact inline chip; `banner` is a full-width strip for
   *  placing under a page hero, so the trigger doesn't float in empty space. */
  variant?: 'pill' | 'banner';
}) {
  const [open, setOpen] = useState(false);

  return (
    <>
      {variant === 'banner' ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          data-testid="credits-guide-trigger"
          className={`group flex w-full items-center gap-3 rounded-2xl border border-primary/20 bg-gradient-to-r from-primary/[0.08] via-primary/[0.04] to-transparent px-4 py-3 text-left shadow-sm transition-colors hover:border-primary/35 hover:from-primary/[0.12] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${className}`}
        >
          <span
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary"
            aria-hidden
          >
            <Coins className="h-[18px] w-[18px]" />
          </span>
          <span className="min-w-0 flex-1">
            <span className="block text-sm font-bold text-navy">{label}</span>
            <span className="mt-0.5 hidden text-xs text-muted sm:block">
              What each exam costs, and when credits are taken.
            </span>
          </span>
          <ChevronRight
            className="h-4 w-4 shrink-0 text-muted transition-transform group-hover:translate-x-0.5"
            aria-hidden
          />
        </button>
      ) : (
        <button
          type="button"
          onClick={() => setOpen(true)}
          data-testid="credits-guide-trigger"
          className={`inline-flex items-center gap-2 rounded-full border border-border bg-surface px-3.5 py-2 text-sm font-semibold text-navy shadow-sm transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:hover:bg-white/5 ${className}`}
        >
          <Coins className="h-4 w-4 text-primary" aria-hidden />
          {label}
        </button>
      )}

      <Modal open={open} onClose={() => setOpen(false)} title="How credits work" size="lg">
        <div className="space-y-5">
          {/* Golden rule */}
          <div className="flex items-start gap-3 rounded-2xl border border-primary/20 bg-primary/5 p-4">
            <span
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary"
              aria-hidden
            >
              <ShieldCheck className="h-5 w-5" />
            </span>
            <div>
              <p className="text-sm font-bold text-navy">You only pay when you start</p>
              <p className="mt-0.5 text-sm text-muted">
                Credits are taken when you begin — and if you’re short, we tell you{' '}
                <span className="font-semibold text-navy">before</span> you start, never in the
                middle of an exam.
              </p>
            </div>
          </div>

          {/* Per-module rows */}
          <ul className="space-y-3">
            {ROWS.map((row) => {
              const accent = ACCENT[row.accent];
              const Icon = row.icon;
              return (
                <li
                  key={row.name}
                  className={`flex items-start gap-3 rounded-2xl border bg-surface p-4 ${accent.ring}`}
                >
                  <span
                    className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${accent.medallion}`}
                    aria-hidden
                  >
                    <Icon className="h-5 w-5" />
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <h3 className="text-sm font-bold text-navy">{row.name}</h3>
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-bold ${accent.pill}`}
                      >
                        {row.cost}
                      </span>
                    </div>
                    <p className="mt-1 text-sm leading-snug text-muted">{row.detail}</p>
                  </div>
                </li>
              );
            })}
          </ul>

          {/* Credit types note */}
          <p className="rounded-2xl border border-dashed border-border px-4 py-3 text-xs leading-relaxed text-muted">
            <span className="font-semibold text-navy">Good to know:</span> some packages give
            all-purpose credits, others are module-specific (e.g. Writing-only). We always spend
            the module-specific ones first, then your all-purpose credits.
          </p>

          {/* Footer */}
          <div className="flex flex-wrap items-center justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={() => setOpen(false)}
              className="rounded-xl px-4 py-2 text-sm font-semibold text-muted transition-colors hover:bg-background-light dark:hover:bg-white/5"
            >
              Got it
            </button>
            <Link
              href="/ai-packages"
              onClick={() => setOpen(false)}
              className="inline-flex items-center gap-2 rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-dark"
            >
              <Coins className="h-4 w-4" aria-hidden />
              Get credits
            </Link>
          </div>
        </div>
      </Modal>
    </>
  );
}
