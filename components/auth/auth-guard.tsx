'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Image from 'next/image';
import { Calendar, CheckCircle2, Lock, Menu, Sparkles, Star } from 'lucide-react';
import type { ReactNode } from 'react';
import type { UserRole } from '@/lib/types/auth';
import { useAuth } from '@/contexts/auth-context';
import { defaultRouteForRole, roleSatisfiesRequired } from '@/lib/auth-routes';
import { useExamDateGate } from '@/hooks/use-exam-date-gate';
import { LearnerDashboardLoadingCard } from '@/components/domain/learner-dashboard-loading';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { learnerMobileNavItems } from '@/components/layout/sidebar';

interface AuthGuardProps {
  children: ReactNode;
  requiredRole?: UserRole;
}

const EXAM_DATE_EXEMPT_PATHS = ['/goals', '/onboarding', '/onboarding-tour'];

function LearnerMobileNavigationLoadingState() {
  return (
    <nav
      aria-hidden="true"
      className="lg:hidden fixed inset-x-2 z-40 glass-panel rounded-[1.25rem] border-border/60 px-1 py-1 shadow-[0_18px_40px_rgba(15,23,42,0.18)] keyboard-safe-floating-bottom"
    >
      <ul className="grid grid-cols-7 gap-1">
        {learnerMobileNavItems.map((item) => (
          <li key={item.href}>
            <span className="relative flex min-h-12 flex-col items-center justify-center gap-0.5 overflow-hidden rounded-[0.85rem] px-1 py-0.5 text-[10px] font-semibold leading-none text-muted">
              <span className="relative z-10 rounded-full p-1 [&_svg]:h-[18px] [&_svg]:w-[18px]">
                {item.icon}
              </span>
              <span className="relative z-10">{item.label}</span>
            </span>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function LearnerSessionLoadingState() {
  return (
    <div
      className="relative isolate flex h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light text-navy"
      role="status"
      aria-busy="true"
      aria-label="Checking your session"
    >
      <div aria-hidden="true" className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute left-1/2 top-0 h-64 w-[42rem] -translate-x-1/2 rounded-full bg-primary/10 blur-3xl" />
        <div className="absolute -left-24 top-24 h-72 w-72 rounded-full bg-info/10 blur-3xl" />
        <div className="absolute bottom-0 right-0 h-72 w-72 rounded-full bg-warning/10 blur-3xl" />
      </div>
      <header className="relative z-10 flex h-14 shrink-0 items-center gap-3 border-b border-border bg-surface px-3 safe-area-inset-top lg:h-24 lg:px-5">
        <div className="flex shrink-0 items-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-full border border-black/[0.06] bg-surface shadow-[0_1px_2px_rgba(15,23,42,0.03)] dark:border-white/[0.08]">
            <Menu className="h-5 w-5 text-muted" aria-hidden="true" />
          </div>
          <Image
            src="/brand/oet-with-dr-hesham-logo.png"
            alt=""
            width={400}
            height={200}
            priority
            className="h-[3.25rem] w-auto object-contain lg:h-[5.5rem]"
          />
        </div>
        <div className="ml-auto flex items-center justify-end gap-1.5 sm:gap-2">
          <div className="h-9 w-9 rounded-full border border-black/[0.06] bg-surface shadow-[0_1px_2px_rgba(15,23,42,0.03)] dark:border-white/[0.08]" />
          <div className="h-9 w-9 rounded-full border border-black/[0.06] bg-surface shadow-[0_1px_2px_rgba(15,23,42,0.03)] dark:border-white/[0.08]" />
          <div className="h-9 w-9 rounded-full border border-black/[0.06] bg-primary/10 shadow-[0_1px_2px_rgba(15,23,42,0.03)] dark:border-white/[0.08]" />
        </div>
      </header>
      <div className="relative z-10 flex min-h-0 flex-1">
        <aside aria-hidden="true" className="hidden w-[104px] shrink-0 border-r border-black/[0.06] bg-surface lg:block" />
        <main className="min-w-0 flex-1 overflow-y-auto overscroll-contain py-4 pb-[calc(var(--bottom-nav-height)+env(safe-area-inset-bottom))] lg:py-6 lg:pb-6">
          <div className="mx-auto w-full max-w-[1200px] px-4 py-2 sm:px-6 sm:py-4 lg:px-8 lg:py-6">
            <div className="space-y-6">
              <LearnerPageHero
                eyebrow="Current Focus"
                icon={Sparkles}
                title="Keep today&apos;s priorities and exam signals in view"
                description="Decide your next action, check your readiness, and move forward with confidence."
                highlights={[
                  { icon: Calendar, label: 'Exam target', value: 'Set your exam date' },
                  { icon: Star, label: 'Pending reviews', value: '0 in progress' },
                  { icon: CheckCircle2, label: "Today's plan", value: 'No tasks scheduled' },
                ]}
                footer={<div aria-hidden="true" className="min-h-[104px]" />}
              />
              <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
                <LearnerDashboardLoadingCard />
              </div>
            </div>
          </div>
        </main>
      </div>
      <LearnerMobileNavigationLoadingState />
    </div>
  );
}

export function AuthGuard({ children, requiredRole }: AuthGuardProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { loading, isAuthenticated, role, pendingMfaChallenge, pendingDeviceChallenge } = useAuth();
  const nextPath = pathname ?? '/';
  const isAuthRoute =
    nextPath === '/sign-in' ||
    nextPath === '/register' ||
    nextPath === '/register/success' ||
    nextPath === '/terms' ||
    nextPath === '/privacy' ||
    nextPath === '/forgot-password' ||
    nextPath === '/forgot-password/verify' ||
    nextPath === '/reset-password' ||
    nextPath === '/reset-password/success' ||
    nextPath === '/verify-email' ||
    nextPath === '/mfa/challenge' ||
    nextPath === '/mfa/recovery' ||
    nextPath === '/mfa/setup' ||
    nextPath === '/device/verify' ||
    nextPath.startsWith('/auth/callback/');

  const examDateGateEnabled =
    !isAuthRoute &&
    isAuthenticated &&
    role === 'learner' &&
    !EXAM_DATE_EXEMPT_PATHS.some((path) => nextPath === path || nextPath.startsWith(`${path}/`));
  const examDateRequired = useExamDateGate(examDateGateEnabled);

  useEffect(() => {
    if (isAuthRoute) {
      return;
    }

    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      router.replace(`/mfa/challenge?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (pendingDeviceChallenge) {
      router.replace(`/device/verify?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (!isAuthenticated) {
      router.replace(`/sign-in?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (requiredRole && !roleSatisfiesRequired(role, requiredRole)) {
      router.replace(role ? defaultRouteForRole(role) : '/');
      return;
    }

    if (examDateGateEnabled && examDateRequired) {
      router.replace('/goals?required=examDate');
    }
  }, [isAuthenticated, isAuthRoute, loading, nextPath, pendingMfaChallenge, pendingDeviceChallenge, requiredRole, role, router, examDateGateEnabled, examDateRequired]);

  if (isAuthRoute) {
    return <>{children}</>;
  }

  const blockedOnExamDate = examDateGateEnabled && examDateRequired === true;

  if (loading || pendingMfaChallenge || pendingDeviceChallenge || !isAuthenticated || (requiredRole && !roleSatisfiesRequired(role, requiredRole)) || blockedOnExamDate) {
    if (requiredRole === 'learner' && !pendingMfaChallenge && !pendingDeviceChallenge && !blockedOnExamDate) {
      return <LearnerSessionLoadingState />;
    }

    return (
      <div className="flex min-h-screen w-full items-center justify-center bg-background-light px-6">
        <div className="flex flex-col items-center gap-4 text-center text-muted">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Lock className="h-6 w-6 animate-pulse" />
          </div>
          <div className="space-y-1">
            <p className="text-sm font-semibold text-navy">Checking your session</p>
            <p className="text-xs text-muted">We&apos;re routing you to the correct workspace.</p>
          </div>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
