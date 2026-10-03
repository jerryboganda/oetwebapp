'use client';

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, LayoutDashboard } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import { HEADER_CHIP, HEADER_CHIP_HOVER } from './header-chrome';

/**
 * The way out of a distraction-free screen.
 *
 * Focus chrome (`resolveLearnerChrome` mode `focus`) and every page that renders
 * `<AppShell distractionFree>` deliberately drop TopNav's brand lockup, the
 * sidebar and the bottom nav — but the header's only other menu trigger is the
 * hamburger, which is `lg:hidden`, so at desktop width an exam had no navigation
 * at all. This module adds the missing exit: Back and Dashboard, at every
 * breakpoint, for learner and staff focus screens alike.
 *
 * An attempt in flight must not be abandoned by a stray click, so a page whose
 * work is live registers with `useFocusExitGuard` and the exit routes through a
 * confirmation. Pages that register nothing exit instantly.
 */

export interface FocusExitGuard {
  /** True while leaving would abandon work in flight (a live attempt). */
  live: boolean;
  title?: string;
  description?: string;
  confirmLabel?: string;
}

type FocusExitTarget = 'back' | 'home';

interface FocusExitContextValue {
  homeHref: string;
  register: (guard: FocusExitGuard | null) => void;
  requestExit: (target: FocusExitTarget) => void;
}

const FocusExitContext = createContext<FocusExitContextValue | null>(null);

const DEFAULT_TITLE = 'Leave this attempt?';
const DEFAULT_DESCRIPTION =
  'Your answers are saved as you go and your timer keeps running. You can come back and continue where you left off.';
const DEFAULT_CONFIRM_LABEL = 'Leave attempt';

/** Same chip as the header's icon buttons: 36px on mobile, 44px from lg up. */
const EXIT_CHIP_CLASS = cn(
  'pressable inline-flex items-center justify-center gap-1.5 text-muted hover:bg-surface hover:text-navy',
  HEADER_CHIP,
  HEADER_CHIP_HOVER,
);

function sameGuard(a: FocusExitGuard | null, b: FocusExitGuard | null) {
  if (a === b) return true;
  if (!a || !b) return false;

  return a.live === b.live
    && a.title === b.title
    && a.description === b.description
    && a.confirmLabel === b.confirmLabel;
}

export function FocusExitProvider({ homeHref, children }: { homeHref: string; children: ReactNode }) {
  const router = useRouter();
  const [guard, setGuard] = useState<FocusExitGuard | null>(null);
  const [pending, setPending] = useState<FocusExitTarget | null>(null);

  const register = useCallback((next: FocusExitGuard | null) => {
    setGuard((current) => (sameGuard(current, next) ? current : next));
  }, []);

  const goTo = useCallback((target: FocusExitTarget) => {
    // A cold start (external link, mobile app launch) has no in-app history, so
    // `back()` would fall out of the app — land on the workspace home instead.
    if (target === 'back' && typeof window !== 'undefined' && window.history.length > 1) {
      router.back();
      return;
    }

    router.push(homeHref);
  }, [homeHref, router]);

  const requestExit = useCallback((target: FocusExitTarget) => {
    if (guard?.live) {
      setPending(target);
      return;
    }

    goTo(target);
  }, [goTo, guard?.live]);

  const leave = () => {
    const target = pending ?? 'home';
    setPending(null);
    goTo(target);
  };

  return (
    <FocusExitContext.Provider value={{ homeHref, register, requestExit }}>
      {children}
      <Modal open={pending !== null} onClose={() => setPending(null)} title={guard?.title ?? DEFAULT_TITLE} size="sm">
        <p className="text-sm leading-relaxed text-muted">{guard?.description ?? DEFAULT_DESCRIPTION}</p>
        <div className="mt-5 flex flex-col gap-2 sm:flex-row-reverse">
          <Button variant="destructive" fullWidth onClick={leave}>
            {guard?.confirmLabel ?? DEFAULT_CONFIRM_LABEL}
          </Button>
          <Button variant="outline" fullWidth onClick={() => setPending(null)}>
            Stay
          </Button>
        </div>
      </Modal>
    </FocusExitContext.Provider>
  );
}

/**
 * Registers the caller's live-attempt state with the focus shell. A no-op when
 * the page is not inside a `FocusExitProvider` (e.g. its own non-distraction-free
 * loading or error state), so it is safe to call unconditionally.
 */
export function useFocusExitGuard(guard: FocusExitGuard) {
  const context = useContext(FocusExitContext);
  const register = context?.register;
  const { live, title, description, confirmLabel } = guard;

  useEffect(() => {
    if (!register) return;

    register({ live, title, description, confirmLabel });
    return () => register(null);
  }, [register, live, title, description, confirmLabel]);
}

/** Back + Dashboard, rendered into the distraction-free TopNav's left cluster. */
export function FocusExitControl() {
  const context = useContext(FocusExitContext);
  const requestExit = context?.requestExit;

  if (!context || !requestExit) return null;

  return (
    <div className="flex items-center gap-1.5">
      <button
        type="button"
        className={cn(EXIT_CHIP_CLASS, 'h-9 w-9 !rounded-full p-0 lg:h-11 lg:w-11')}
        onClick={() => requestExit('back')}
        aria-label="Go back"
      >
        <ArrowLeft className="h-[18px] w-[18px] rtl:rotate-180 lg:h-5 lg:w-5" aria-hidden="true" />
      </button>
      <Link
        href={context.homeHref}
        prefetch={false}
        className={cn(EXIT_CHIP_CLASS, 'h-9 rounded-full px-2.5 lg:h-11 lg:px-3.5')}
        onClick={(event) => {
          // Let the shell decide: a live attempt confirms first.
          event.preventDefault();
          requestExit('home');
        }}
      >
        <LayoutDashboard className="h-[18px] w-[18px] lg:h-5 lg:w-5" aria-hidden="true" />
        <span className="hidden text-xs font-semibold sm:inline lg:text-sm">Dashboard</span>
      </Link>
    </div>
  );
}
