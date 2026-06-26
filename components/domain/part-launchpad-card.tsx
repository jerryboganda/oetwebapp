'use client';

import Link from 'next/link';
import { ArrowRight, Clock, FileText, Lock, Play, ShieldAlert, Sparkles, Target, TrendingUp } from 'lucide-react';
import { Card } from '@/components/ui';
import { cn } from '@/lib/utils';
import type {
  ReadingHomeAttemptDto,
  ReadingHomeDto,
  ReadingHomePaperDto,
  ReadingHomeSafeDrillDto,
} from '@/lib/reading-authoring-api';

export type PartCode = 'A' | 'B' | 'C';

interface PartLaunchpadCardProps {
  partCode: PartCode;
  home: ReadingHomeDto;
  errorBankCount: number;
}

interface PartCopy {
  title: string;
  description: string;
  tip: string;
  itemCount: number;
  optionsLabel: string;
  icon: typeof Clock;
  accent: 'amber' | 'blue' | 'indigo';
}

const PART_COPY: Record<PartCode, PartCopy> = {
  A: {
    title: 'Lock exact details first',
    description: 'Rapid extraction across four medical texts. Use the opening window before moving into the longer B/C block.',
    tip: 'Headings are word-anchors. Scan, don\u2019t read.',
    itemCount: 20,
    optionsLabel: 'Strict timer',
    icon: Clock,
    accent: 'amber',
  },
  B: {
    title: 'Read the purpose of each extract',
    description: 'Short extracts from healthcare policies, notices, guidelines and clinical communications. Pick the option supported by exact wording.',
    tip: 'Match meaning, not vocabulary.',
    itemCount: 6,
    optionsLabel: '3 options',
    icon: FileText,
    accent: 'blue',
  },
  C: {
    title: 'Control inference choices',
    description: 'Separate stated evidence from tempting distractors while holding the main argument of each passage.',
    tip: 'When two options look right, pick the one with stated evidence.',
    itemCount: 16,
    optionsLabel: '4 options',
    icon: TrendingUp,
    accent: 'indigo',
  },
};

const ACCENT_TOKENS = {
  amber: {
    border: 'border-amber-200',
    bgSoft: 'bg-amber-50',
    bgIcon: 'bg-amber-100',
    text: 'text-amber-800',
    textStrong: 'text-amber-900',
    eyebrowBg: 'bg-amber-100',
    eyebrowText: 'text-amber-900',
    button: 'bg-amber-600 hover:bg-amber-700 text-white',
    secondaryText: 'text-amber-800 hover:text-amber-900',
  },
  blue: {
    border: 'border-blue-200',
    bgSoft: 'bg-blue-50',
    bgIcon: 'bg-blue-100',
    text: 'text-blue-800',
    textStrong: 'text-blue-900',
    eyebrowBg: 'bg-blue-100',
    eyebrowText: 'text-blue-900',
    button: 'bg-blue-600 hover:bg-blue-700 text-white',
    secondaryText: 'text-blue-800 hover:text-blue-900',
  },
  indigo: {
    border: 'border-indigo-200',
    bgSoft: 'bg-indigo-50',
    bgIcon: 'bg-indigo-100',
    text: 'text-indigo-800',
    textStrong: 'text-indigo-900',
    eyebrowBg: 'bg-indigo-100',
    eyebrowText: 'text-indigo-900',
    button: 'bg-indigo-600 hover:bg-indigo-700 text-white',
    secondaryText: 'text-indigo-800 hover:text-indigo-900',
  },
} as const;

interface Pill {
  icon: typeof Play;
  label: string;
  emphasise?: boolean;
}

interface PrimaryAction {
  label: string;
  href: string;
}

interface LaunchpadState {
  pill: Pill;
  primary: PrimaryAction;
}

function pickPaperForActiveAttempt(
  papers: ReadingHomePaperDto[],
  attempt: ReadingHomeAttemptDto,
): ReadingHomePaperDto | null {
  return papers.find((p) => p.id === attempt.paperId) ?? null;
}

function pickSafeDrillForPart(
  drills: ReadingHomeSafeDrillDto[],
  partCode: PartCode,
): ReadingHomeSafeDrillDto | null {
  const wanted = `Part ${partCode}`.toLowerCase();
  return drills.find((d) => (d.focusLabel ?? '').toLowerCase() === wanted) ?? null;
}

function pickAccessiblePaper(papers: ReadingHomePaperDto[]): ReadingHomePaperDto | null {
  return (
    papers.find((p) => !p.entitlement || p.entitlement.allowed) ??
    papers[0] ??
    null
  );
}

function computeState(props: PartLaunchpadCardProps): LaunchpadState {
  const { partCode, home, errorBankCount } = props;
  const papers = home.papers ?? [];
  const activeAttempts = home.activeAttempts ?? [];
  const safeDrills = home.safeDrills ?? [];

  // 1) Active attempt on a paper → resume covers all parts (A/B/C share the attempt).
  const activeAttempt = activeAttempts.find((a) => a.canResume) ?? activeAttempts[0];
  if (activeAttempt) {
    return {
      pill: {
        icon: Play,
        label: `Resume \u2014 ${activeAttempt.answeredCount}/${activeAttempt.totalQuestions} answered`,
        emphasise: true,
      },
      primary: {
        label: 'Resume attempt',
        href: activeAttempt.route,
      },
    };
  }

  // 2) Targeted safe-drill recommended for this part by the backend.
  const drill = pickSafeDrillForPart(safeDrills, partCode);
  if (drill) {
    return {
      pill: {
        icon: Target,
        label: drill.title,
        emphasise: true,
      },
      primary: {
        label: `Open Part ${partCode} drill`,
        href: drill.launchRoute,
      },
    };
  }

  // 3) Error-bank items waiting for this part.
  if (errorBankCount > 0) {
    return {
      pill: {
        icon: ShieldAlert,
        label: `${errorBankCount} item${errorBankCount === 1 ? '' : 's'} in your error bank`,
      },
      primary: {
        label: `Review Part ${partCode} errors`,
        href: `/reading/practice?focus=${partCode}&tab=errors`,
      },
    };
  }

  // 4) Entitlement-locked across the board.
  const accessible = pickAccessiblePaper(papers);
  if (papers.length > 0 && accessible && accessible.entitlement && !accessible.entitlement.allowed) {
    return {
      pill: { icon: Lock, label: 'Upgrade to unlock Reading papers' },
      primary: { label: 'View plans', href: '/subscriptions' },
    };
  }

  // 5) Ready papers available → start the first accessible one.
  if (accessible) {
    return {
      pill: {
        icon: Sparkles,
        label: `${papers.length} paper${papers.length === 1 ? '' : 's'} ready \u2014 each contains Part ${partCode}`,
      },
      primary: {
        label: 'Start a paper',
        href: accessible.route,
      },
    };
  }

  // 6) No papers yet (rare — admin hasn\u2019t published).
  return {
    pill: { icon: Sparkles, label: 'Reading papers coming soon' },
    primary: { label: 'Open Practice Hub', href: '/reading/practice' },
  };
}

export function PartLaunchpadCard(props: PartLaunchpadCardProps) {
  const { partCode, home, errorBankCount } = props;
  const copy = PART_COPY[partCode];
  const tokens = ACCENT_TOKENS[copy.accent];
  const Icon = copy.icon;
  const state = computeState(props);
  const PillIcon = state.pill.icon;

  const timerLabel = partCode === 'A'
    ? `${home.policy.partATimerMinutes ?? 15}-min strict`
    : `Shared ${home.policy.partBCTimerMinutes ?? 45}-min`;

  const secondaryHref = `/reading/practice?focus=${partCode}`;

  return (
    <Card
      className={cn(
        'flex h-full flex-col gap-4 border p-5 transition-shadow hover:shadow-md',
        tokens.border,
      )}
    >
      {/* Header — eyebrow + live timer chip */}
      <div className="flex items-start justify-between gap-3">
        <span
          className={cn(
            'inline-flex items-center gap-1.5 rounded-md border border-transparent px-2 py-1 text-xs font-bold uppercase tracking-wide',
            tokens.eyebrowBg,
            tokens.eyebrowText,
          )}
        >
          <Icon className="h-3.5 w-3.5" aria-hidden />
          Part {partCode}
        </span>
        <span className="inline-flex items-center gap-1 rounded-full border border-border bg-surface px-2.5 py-1 text-[11px] font-semibold text-foreground">
          <Clock className="h-3 w-3" aria-hidden />
          {timerLabel}
        </span>
      </div>

      {/* Title + description */}
      <div>
        <h3 className={cn('text-base font-bold leading-snug', tokens.textStrong)}>{copy.title}</h3>
        <p className="mt-1.5 text-sm leading-relaxed text-muted">{copy.description}</p>
      </div>

      {/* Status pill */}
      <div
        className={cn(
          'inline-flex w-fit items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold',
          state.pill.emphasise ? cn(tokens.bgIcon, tokens.textStrong) : cn(tokens.bgSoft, tokens.text),
        )}
      >
        <PillIcon className="h-3.5 w-3.5 shrink-0" aria-hidden />
        <span className="truncate">{state.pill.label}</span>
      </div>

      {/* Item-count + options metadata (kept) */}
      <div className="flex items-center gap-3 text-xs text-muted">
        <span className="inline-flex items-center gap-1">
          <Target className="h-3.5 w-3.5" aria-hidden />
          {copy.itemCount} items
        </span>
        <span className="text-border" aria-hidden="true">{'\u00b7'}</span>
        <span>{copy.optionsLabel}</span>
      </div>

      {/* Tactical micro-tip */}
      <p className="text-xs italic text-muted">
        <span className="not-italic font-semibold text-foreground">Tip:</span> {copy.tip}
      </p>

      {/* Footer — primary CTA + secondary link */}
      <div className="mt-auto flex flex-col gap-2 pt-2 sm:flex-row sm:items-center sm:justify-between">
        <Link
          href={state.primary.href}
          className={cn(
            'inline-flex min-h-11 items-center justify-center gap-1.5 rounded-lg px-4 py-2 text-sm font-semibold transition-colors',
            tokens.button,
          )}
        >
          {state.primary.label}
          <ArrowRight className="h-4 w-4" aria-hidden />
        </Link>
        {errorBankCount > 0 ? (
          <Link
            href={`/reading/practice?focus=${partCode}&tab=errors`}
            className={cn('text-xs font-semibold underline-offset-4 hover:underline', tokens.secondaryText)}
          >
            Review {errorBankCount} Part {partCode} error{errorBankCount === 1 ? '' : 's'}
          </Link>
        ) : (
          <Link
            href={secondaryHref}
            className={cn('text-xs font-semibold underline-offset-4 hover:underline', tokens.secondaryText)}
          >
            Browse Part {partCode} drills
          </Link>
        )}
      </div>
    </Card>
  );
}
