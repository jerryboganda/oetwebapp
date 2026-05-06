'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { ChevronRight, Home } from 'lucide-react';
import { shouldShowLearnerBreadcrumbs } from '@/components/layout/learner-dashboard-route-policy';
import { cn } from '@/lib/utils';

const routeLabelOverrides: Record<string, string> = {
  achievements: 'Achievements',
  billing: 'Billing',
  cards: 'Cards',
  check: 'Mic Check',
  compare: 'Compare',
  conversation: 'AI Conversation',
  diagnostic: 'Diagnostic',
  discover: 'Discover',
  drills: 'Drills',
  expert: 'Expert',
  'expert-request': 'Expert Request',
  feedback: 'Feedback',
  goals: 'Goals',
  grammar: 'Grammar',
  library: 'Library',
  listening: 'Listening',
  mocks: 'Mocks',
  model: 'Model Answer',
  onboarding: 'Onboarding',
  paper: 'Paper',
  phrasing: 'Phrasing',
  progress: 'Progress',
  readiness: 'Readiness',
  reading: 'Reading',
  recalls: 'Recalls',
  report: 'Report',
  result: 'Result',
  results: 'Results',
  review: 'Review',
  revision: 'Revision',
  roleplay: 'Roleplay',
  selection: 'Selection',
  settings: 'Settings',
  setup: 'Setup',
  speaking: 'Speaking',
  strategies: 'Strategies',
  'study-plan': 'Study Plan',
  submissions: 'Submissions',
  transcript: 'Transcript',
  words: 'Words',
  writing: 'Writing',
};

function humanizeSegment(segment: string) {
  if (/^[0-9a-f-]{8,}$/i.test(segment)) {
    return 'Detail';
  }

  return routeLabelOverrides[segment] ?? segment
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

export function LearnerBreadcrumbs({ className, labelOverrides }: { className?: string; labelOverrides?: Record<string, string> }) {
  const pathname = usePathname();

  if (!shouldShowLearnerBreadcrumbs(pathname)) {
    return null;
  }

  const segments = (pathname ?? '').split('/').filter(Boolean);
  const crumbs = segments.map((segment, index) => {
    const href = `/${segments.slice(0, index + 1).join('/')}`;
    return {
      href,
      label: labelOverrides?.[href] ?? humanizeSegment(segment),
      current: index === segments.length - 1,
    };
  });

  return (
    <nav className={cn('mb-3 text-xs font-semibold text-muted sm:mb-4', className)} aria-label="Breadcrumb">
      <ol className="flex min-w-0 flex-wrap items-center gap-1.5">
        <li>
          <Link
            href="/"
            className="inline-flex min-h-7 items-center gap-1 rounded-full px-2 text-muted transition-colors hover:bg-white/80 hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
          >
            <Home className="h-3.5 w-3.5" aria-hidden="true" />
            Dashboard
          </Link>
        </li>
        {crumbs.map((crumb) => (
          <li key={crumb.href} className="flex min-w-0 items-center gap-1.5">
            <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted/60" aria-hidden="true" />
            {crumb.current ? (
              <span className="truncate rounded-full bg-white/70 px-2 py-1 text-navy" aria-current="page">
                {crumb.label}
              </span>
            ) : (
              <Link
                href={crumb.href}
                className="truncate rounded-full px-2 py-1 text-muted transition-colors hover:bg-white/80 hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
              >
                {crumb.label}
              </Link>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}
