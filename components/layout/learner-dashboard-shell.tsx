'use client';

import { createContext, useContext, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { MotionConfig } from 'motion/react';
import { usePathname } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { LearnerBreadcrumbs } from '@/components/domain/learner-breadcrumbs';
import { AppShell, type AppShellProps } from './app-shell';
import { resolveLearnerChrome } from './learner-dashboard-route-policy';
import { LearnerWorkspaceContainer } from './learner-workspace-container';
import { learnerMainNavItems, learnerMobileNavItems } from './sidebar';

/**
 * The TopNav actions element of the layout-owned learner shell: `undefined`
 * outside app/(learner)/layout.tsx, `null` inside it until the header mounts.
 */
const LearnerShellSlot = createContext<HTMLElement | null | undefined>(undefined);

/** Renders page-owned header actions into the learner shell's TopNav. */
export function LearnerNavActions({ children }: { children: ReactNode }) {
  const slot = useContext(LearnerShellSlot);
  return slot ? createPortal(children, slot) : null;
}

export interface LearnerDashboardShellProps extends AppShellProps {
  workspaceClassName?: string;
  children: ReactNode;
}

export function LearnerDashboardShell({
  children,
  workspaceClassName,
  distractionFree,
  mobileMenuSections,
  mobileNavItems,
  navItems,
  navActions,
  ...shellProps
}: LearnerDashboardShellProps) {
  // Learner sidebar: Dashboard | Listening | Reading | Writing | Speaking |
  // Mocks | Recalls | Progress | Billing. The legacy Learn group
  // (Grammar/Classes/Lessons/Strategies/Conversation) is not surfaced in the
  // candidate workspace.
  const learnerNavItems = navItems ?? learnerMainNavItems;
  const learnerMobileMenuSections = mobileMenuSections ?? [
    { label: 'Practice', items: learnerNavItems },
  ];
  const learnerBottomNavItems = mobileNavItems ?? learnerMobileNavItems;

  return (
    <AppShell
      requiredRole="learner"
      distractionFree={distractionFree}
      navActions={navActions}
      {...shellProps}
      navItems={learnerNavItems}
      mobileNavItems={learnerBottomNavItems}
      mobileMenuSections={learnerMobileMenuSections}
      workspaceRole="learner"
    >
      <LearnerWorkspaceContainer className={workspaceClassName}>
        {distractionFree ? children : (
          <>
            <LearnerBreadcrumbs />
            <div className="learner-page-flow">{children}</div>
          </>
        )}
      </LearnerWorkspaceContainer>
    </AppShell>
  );
}

/**
 * Exam and live routes never animate (DESIGN.md §5). One switch covers both
 * motion/react (MotionConfig) and CSS (the `[data-motion="still"]` rules that
 * mirror the in-app Reduce motion setting). `display: contents` keeps layout.
 */
function ExamStillness({ still, children }: { still: boolean; children: ReactNode }) {
  if (!still) return children;
  return (
    <MotionConfig reducedMotion="always">
      <div data-motion="still" className="contents">{children}</div>
    </MotionConfig>
  );
}

/**
 * The learner shell for every page in app/(learner), mounted once by its
 * layout so TopNav, Sidebar, BottomNav and AuthGuard persist across learner
 * navigations. The chrome comes from the URL (resolveLearnerChrome):
 * self-chromed routes render bare, focus routes distraction-free. Pages add
 * header actions through LearnerNavActions.
 */
export function LearnerShellLayout({ children }: { children: ReactNode }) {
  const t = useTranslations();
  const chrome = resolveLearnerChrome(usePathname());
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const page = <ExamStillness still={chrome.examOrLive}>{children}</ExamStillness>;

  if (chrome.mode === 'none') return page;

  return (
    <LearnerDashboardShell
      distractionFree={chrome.mode === 'focus'}
      requireAuth={chrome.requireAuth}
      pageTitle={chrome.titleKey ? t(chrome.titleKey) : chrome.title}
      navActions={<span ref={setSlot} className="contents" />}
    >
      <LearnerShellSlot.Provider value={slot}>{page}</LearnerShellSlot.Provider>
    </LearnerDashboardShell>
  );
}
