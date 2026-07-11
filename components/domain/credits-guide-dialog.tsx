'use client';

import { useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  ClipboardCheck,
  Coins,
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
// spend credits. The numbers here mirror the live billing rules:
//   Reading / Listening — 1 credit per PAPER (parts + re-tries free)
//   Writing / Speaking  — 2 credits per exam (no parts)
//   Speaking practice   — 1 credit per single card
//   Mock                — 1 mock credit; Writing/Speaking are tutor-marked

type Accent = 'blue' | 'violet' | 'amber' | 'emerald' | 'rose';

const ACCENT: Record<Accent, { medallion: string; pill: string; ring: string }> = {
  blue: {
    medallion: 'bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-200',
    pill: 'bg-blue-100 text-blue-800 dark:bg-blue-900/50 dark:text-blue-100',
    ring: 'border-blue-100 dark:border-blue-900/40',
  },
  violet: {
    medallion: 'bg-violet-100 text-violet-700 dark:bg-violet-900/50 dark:text-violet-200',
    pill: 'bg-violet-100 text-violet-800 dark:bg-violet-900/50 dark:text-violet-100',
    ring: 'border-violet-100 dark:border-violet-900/40',
  },
  amber: {
    medallion: 'bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-200',
    pill: 'bg-amber-100 text-amber-900 dark:bg-amber-900/50 dark:text-amber-100',
    ring: 'border-amber-100 dark:border-amber-900/40',
  },
  emerald: {
    medallion: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/50 dark:text-emerald-200',
    pill: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/50 dark:text-emerald-100',
    ring: 'border-emerald-100 dark:border-emerald-900/40',
  },
  rose: {
    medallion: 'bg-rose-100 text-rose-700 dark:bg-rose-900/50 dark:text-rose-200',
    pill: 'bg-rose-100 text-rose-800 dark:bg-rose-900/50 dark:text-rose-100',
    ring: 'border-rose-100 dark:border-rose-900/40',
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
    detail: 'Charged per exam — one AI-marked letter. Writing has no parts.',
  },
  {
    icon: Mic,
    accent: 'emerald',
    name: 'Speaking',
    cost: '2 credits',
    detail:
      'Charged per exam — the full AI role-play. A single practice card on its own is just 1 credit.',
  },
  {
    icon: ClipboardCheck,
    accent: 'rose',
    name: 'Mock exam',
    cost: '1 mock credit',
    detail:
      'A full mock uses one Mock credit. Writing & Speaking in a mock are marked by a real tutor, so they don’t use your AI credits.',
  },
];

export function CreditsGuideButton({
  className = '',
  label = 'How credits work',
}: {
  className?: string;
  label?: string;
}) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        data-testid="credits-guide-trigger"
        className={`inline-flex items-center gap-2 rounded-full border border-border bg-surface px-3.5 py-2 text-sm font-semibold text-navy shadow-sm transition-colors hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:hover:bg-white/5 ${className}`}
      >
        <Coins className="h-4 w-4 text-primary" aria-hidden />
        {label}
      </button>

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
