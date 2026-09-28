'use client';

import { cn } from '@/lib/utils';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import type { UserRole } from '@/lib/types/auth';
import { trackHelpCenterOpened } from '@/lib/onboarding/tour-events';
import { UserAvatar } from '@/components/ui/user-avatar';
import { HelpCenterDrawer } from '@/components/onboarding/help-center-drawer';
import { ChevronDown, HelpCircle, LogOut, Settings } from 'lucide-react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useRef, useState } from 'react';
import { HEADER_CHIP, HEADER_CHIP_HOVER } from './header-chrome';

const MENU_ITEM_CLASS =
  'flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40';

/** Avatar + name + role with a small account menu (top-nav header). */
export function ProfileMenu({
  displayName,
  avatarUrl,
  email,
  roleLabel,
  settingsHref,
  onSignOut,
  workspaceRole,
}: {
  displayName: string;
  avatarUrl?: string | null;
  email: string;
  roleLabel: string;
  settingsHref: string;
  onSignOut?: () => void;
  workspaceRole?: UserRole;
}) {
  const [open, setOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  // The header persists across navigations, so close the menu and help drawer
  // when the route changes (adjusted during render, no effect).
  const pathname = usePathname();
  const [openedAt, setOpenedAt] = useState(pathname);
  if (openedAt !== pathname) {
    setOpenedAt(pathname);
    setOpen(false);
    setHelpOpen(false);
  }
  const containerRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const menuMotion = getSurfaceMotion('overlay', reducedMotion);

  useEffect(() => {
    if (!open) return undefined;
    const onPointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
        // Return focus to the trigger so keyboard users aren't dropped to <body>.
        triggerRef.current?.focus();
      }
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  return (
    <div className="relative" ref={containerRef}>
      <button
        ref={triggerRef}
        type="button"
        // The dashboard tour anchors on this attribute to show learners where
        // to replay tours; help lives in this menu now, so the anchor moves
        // here with it.
        data-tour="learner-help-launcher"
        onClick={() => setOpen((current) => !current)}
        aria-haspopup="menu"
        aria-expanded={open}
        className={cn(
          'flex items-center gap-2.5 rounded-full p-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 lg:rounded-xl lg:p-1.5 lg:pr-3',
          HEADER_CHIP,
          HEADER_CHIP_HOVER,
        )}
      >
        <UserAvatar avatarUrl={avatarUrl} displayName={displayName} className="h-7 w-7 lg:h-9 lg:w-9" />
        <span className="hidden min-w-0 text-left leading-tight xl:block">
          <span className="block max-w-[9rem] truncate text-[13px] font-bold text-navy">{displayName}</span>
          <span className="block text-2xs text-muted">{roleLabel}</span>
        </span>
        <ChevronDown
          className={cn('hidden h-4 w-4 shrink-0 text-muted transition-transform duration-200 ease-standard lg:block', open && 'rotate-180')}
          aria-hidden="true"
        />
      </button>

      <AnimatePresence initial={false}>
        {open ? (
          <motion.div
            role="menu"
            {...menuMotion}
            style={{ transformOrigin: 'top right' }}
            className="absolute right-0 top-[calc(100%+0.5rem)] z-50 w-56 overflow-hidden rounded-xl border border-border bg-surface p-1.5 shadow-lg"
          >
            <div className="border-b border-border px-2.5 pb-2 pt-1.5">
              <p className="truncate text-[13px] font-bold text-navy">{displayName}</p>
              {email ? <p className="truncate text-2xs text-muted">{email}</p> : null}
            </div>
            <Link
              href={settingsHref}
              prefetch={false}
              role="menuitem"
              onClick={() => setOpen(false)}
              className={cn(MENU_ITEM_CLASS, 'mt-1 text-navy hover:bg-background-light')}
            >
              <Settings className="h-4 w-4 text-muted" aria-hidden="true" />
              Settings
            </Link>
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                setHelpOpen(true);
                trackHelpCenterOpened({
                  role: workspaceRole,
                  route: typeof window !== 'undefined' ? window.location.pathname : undefined,
                });
              }}
              className={cn(MENU_ITEM_CLASS, 'text-navy hover:bg-background-light')}
              aria-haspopup="dialog"
            >
              <HelpCircle className="h-4 w-4 text-muted" aria-hidden="true" />
              Help &amp; guided tours
            </button>
            {onSignOut ? (
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setOpen(false);
                  onSignOut();
                }}
                className={cn(MENU_ITEM_CLASS, 'text-danger hover:bg-danger/10')}
              >
                <LogOut className="h-4 w-4" aria-hidden="true" />
                Sign out
              </button>
            ) : null}
          </motion.div>
        ) : null}
      </AnimatePresence>

      <HelpCenterDrawer open={helpOpen} onClose={() => setHelpOpen(false)} workspaceRole={workspaceRole} />
    </div>
  );
}
