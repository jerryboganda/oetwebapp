'use client';

import type { ReactNode } from 'react';
import Link from 'next/link';
import { ChevronRight } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { Badge } from '@/components/ui/badge';

// The FREE SAMPLE entry card shown before the main practice options of a
// module (Free Mocks proposal, 2026-09). Styled like the "How credits work"
// banner (CreditsGuideButton): soft purple tint, rounded card, icon tile,
// short title + one sentence, chevron. Renders a link when `href` is given,
// otherwise a button (Writing/Speaking open a profession picker first).
//
// Import this by its direct path, not the '@/components/domain' barrel — the
// hub page tests mock the barrel with only the exports they need.

const CARD_CLASS =
  'group flex w-full items-center gap-3 rounded-2xl border border-violet-200 bg-gradient-to-r from-violet-50 via-violet-50/60 to-transparent px-4 py-3.5 text-left shadow-sm transition-colors hover:border-violet-300 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary dark:border-violet-900/50 dark:from-violet-950/40 dark:via-violet-950/20';

export interface FreeSampleCardProps {
  title: string;
  description: string;
  icon: LucideIcon;
  testId: string;
  /** Link target. Omit (and pass `onClick`) to render a button instead. */
  href?: string;
  onClick?: () => void;
  /** Extra content under the sentence (e.g. a "Used" hint). */
  footer?: ReactNode;
  className?: string;
}

export function FreeSampleCard({
  title,
  description,
  icon: Icon,
  testId,
  href,
  onClick,
  footer,
  className = '',
}: FreeSampleCardProps) {
  const body = (
    <>
      <span
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-violet-100 text-violet-700 dark:bg-violet-900/50 dark:text-violet-200"
        aria-hidden
      >
        <Icon className="h-5 w-5" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-bold text-navy">{title}</span>
          <Badge variant="violet" size="sm" className="uppercase tracking-wide">
            Free sample
          </Badge>
        </span>
        <span className="mt-0.5 block text-xs text-muted sm:text-sm">{description}</span>
        {footer}
      </span>
      <ChevronRight
        className="h-4 w-4 shrink-0 text-muted transition-transform group-hover:translate-x-0.5 rtl:rotate-180 rtl:group-hover:-translate-x-0.5"
        aria-hidden
      />
    </>
  );

  if (href) {
    return (
      <Link href={href} data-testid={testId} onClick={onClick} className={`${CARD_CLASS} ${className}`}>
        {body}
      </Link>
    );
  }
  return (
    <button type="button" data-testid={testId} onClick={onClick} className={`${CARD_CLASS} ${className}`}>
      {body}
    </button>
  );
}
