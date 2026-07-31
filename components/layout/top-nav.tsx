'use client';

import { getMotionDelay, getMotionPresenceMode, getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { cn } from '@/lib/utils';
import { buildSupportMailto } from '@/lib/auth/support';
import { AuthContext } from '@/contexts/auth-context';
import { collectFeatureFlagKeys, isFeatureFlaggedItemVisible, useFeatureFlagMap } from '@/hooks/use-feature-flag-map';
import type { UserRole } from '@/lib/types/auth';
import { ChevronDown, HelpCircle, LogOut, Menu, Settings, X } from 'lucide-react';
import Image from 'next/image';
import Link from 'next/link';
import { type ReactNode, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { GlobalSearch } from './global-search';
import { HEADER_CHIP, HEADER_CHIP_HOVER } from './header-chrome';
import { getWorkspaceSettingsHref, mainNavItems, type NavItem, type ShellUserSummary } from './sidebar';
import { usePathname, useRouter } from 'next/navigation';
import { NotificationCenter } from './notification-center';
import { LearnerStreakBadges } from './learner-streak-badges';
import { UserAvatar } from '@/components/ui/user-avatar';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { getSurfaceTransition } from '@/lib/motion';
import { ThemeToggle } from '@/components/ui/theme-toggle';
import { TourLauncher } from '@/components/onboarding/tour-launcher';
import { HelpCenterDrawer } from '@/components/onboarding/help-center-drawer';
import { trackHelpCenterOpened } from '@/lib/onboarding/tour-events';

export interface MobileMenuSection {
  label: string;
  items: NavItem[];
}

interface TopNavProps {
  pageTitle?: string;
  className?: string;
  actions?: ReactNode;
  items?: NavItem[];
  sectionedItems?: MobileMenuSection[];
  userSummary?: ShellUserSummary;
  workspaceRole?: UserRole;
  /** Renders the brand lockup + global search. Set when the header spans the
   *  full viewport width (learner workspace) rather than sitting beside the
   *  sidebar, which owns the logo in the other workspaces. */
  showBrand?: boolean;
  /** Desktop sidebar collapse. Supplying this reveals the menu button at lg+,
   *  where it would otherwise be a mobile-only control. */
  onToggleSidebar?: () => void;
  sidebarCollapsed?: boolean;
}

/** Bordered circular icon button used by the bell and theme toggle.
 *  Compact (36px) on mobile so the header reads like a native app bar,
 *  full-size (44px) from lg up. */
const ICON_BUTTON_CLASS = cn(
  'h-9 w-9 lg:h-11 lg:w-11 !rounded-full p-0 text-muted hover:bg-surface hover:text-navy',
  '[&_svg]:h-[18px] [&_svg]:w-[18px] lg:[&_svg]:h-5 lg:[&_svg]:w-5',
  HEADER_CHIP,
  HEADER_CHIP_HOVER,
);

const ROLE_LABEL: Record<string, string> = {
  learner: 'Learner',
  expert: 'Tutor',
  admin: 'Admin',
};

/** Avatar + name + role with a small account menu. */
function ProfileMenu({
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
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return undefined;
    const onPointerDown = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
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
          <span className="block text-[11px] text-muted">{roleLabel}</span>
        </span>
        <ChevronDown className="hidden h-4 w-4 shrink-0 text-muted lg:block" aria-hidden="true" />
      </button>

      {open ? (
        <div
          role="menu"
          className="absolute right-0 top-[calc(100%+0.5rem)] z-50 w-56 overflow-hidden rounded-xl border border-border bg-surface p-1.5 shadow-xl"
        >
          <div className="border-b border-border px-2.5 pb-2 pt-1.5">
            <p className="truncate text-[13px] font-bold text-navy">{displayName}</p>
            {email ? <p className="truncate text-[11.5px] text-muted">{email}</p> : null}
          </div>
          <Link
            href={settingsHref}
            role="menuitem"
            onClick={() => setOpen(false)}
            className="mt-1 flex items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-medium text-navy transition-colors hover:bg-background-light"
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
            className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-medium text-navy transition-colors hover:bg-background-light"
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
              className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-medium text-danger transition-colors hover:bg-danger/10"
            >
              <LogOut className="h-4 w-4" aria-hidden="true" />
              Sign out
            </button>
          ) : null}
        </div>
      ) : null}

      <HelpCenterDrawer open={helpOpen} onClose={() => setHelpOpen(false)} workspaceRole={workspaceRole} />
    </div>
  );
}

function matchesPathPrefix(pathname: string, prefix: string): boolean {
  const normalizedPrefix = prefix !== '/' && prefix.endsWith('/') ? prefix.slice(0, -1) : prefix;
  return pathname === normalizedPrefix || pathname.startsWith(`${normalizedPrefix}/`);
}

function getActiveMatchLength(pathname: string, item: NavItem): number {
  if (item.exact) return pathname === item.href ? Number.MAX_SAFE_INTEGER : -1;
  if (item.href === '/') return pathname === '/' ? 1 : -1;

  const prefix = item.matchPrefix ?? item.href;
  return matchesPathPrefix(pathname, prefix) ? prefix.length : -1;
}

function getActiveHref(pathname: string, items: NavItem[]): string | null {
  let activeHref: string | null = null;
  let activeLength = -1;

  for (const item of items) {
    const matchLength = getActiveMatchLength(pathname, item);
    if (matchLength > activeLength) {
      activeHref = item.href;
      activeLength = matchLength;
    }
  }

  return activeHref;
}

export function TopNav({
  pageTitle,
  className,
  actions,
  items = mainNavItems,
  sectionedItems,
  userSummary,
  workspaceRole,
  showBrand = false,
  onToggleSidebar,
  sidebarCollapsed = false,
}: TopNavProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const pathname = usePathname() ?? '/';
  const router = useRouter();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const displayName = userSummary?.displayName?.trim() || 'User';
  const initials = displayName.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase();
  const authContext = useContext(AuthContext);
  const signOut = authContext?.signOut;
  const shouldFilterFeatures = workspaceRole === 'learner';
  const sectionFeatureKeys = useMemo(
    () => collectFeatureFlagKeys(sectionedItems?.flatMap((section) => section.items) ?? []),
    [sectionedItems],
  );
  const learnerFeatureFlags = useFeatureFlagMap(sectionFeatureKeys, shouldFilterFeatures && Boolean(sectionedItems?.length));
  const visibleSectionedItems = useMemo(
    () =>
      sectionedItems
        ?.map((section) => ({
          ...section,
          items: section.items.filter((item) => isFeatureFlaggedItemVisible(item, learnerFeatureFlags, shouldFilterFeatures)),
        }))
        .filter((section) => section.items.length > 0),
    [learnerFeatureFlags, sectionedItems, shouldFilterFeatures],
  );
  const sectionedActiveHref = useMemo(
    () => getActiveHref(pathname, visibleSectionedItems?.flatMap((section) => section.items) ?? []),
    [pathname, visibleSectionedItems],
  );
  const flatActiveHref = useMemo(() => getActiveHref(pathname, items), [items, pathname]);
  const hasSectionedMenu = Boolean(sectionedItems?.length);
  const pathnameLower = pathname.toLowerCase();
  const isPrivilegedPath = pathnameLower.startsWith('/expert') || pathnameLower.startsWith('/admin');
  const showStreakBadges = workspaceRole === 'learner' && !isPrivilegedPath;

  const menuMotionProps = reducedMotion
    ? {
        initial: false,
        animate: { opacity: 1 },
        exit: { opacity: 0 },
        transition: getSurfaceTransition('state', true),
      }
    : {
        initial: { opacity: 0, x: -20, scale: 0.99 },
        animate: { opacity: 1, x: 0, scale: 1 },
        exit: { opacity: 0, x: -20, scale: 0.99 },
        transition: getSurfaceTransition('overlay', false),
      };
  const overlayMotionProps = reducedMotion
    ? { initial: false, animate: { opacity: 1 }, exit: { opacity: 0 }, transition: getSurfaceTransition('state', true) }
    : { initial: { opacity: 0 }, animate: { opacity: 1 }, exit: { opacity: 0 }, transition: getSurfaceTransition('state', false) };
  const itemMotionProps = getSurfaceMotion('item', reducedMotion);
  const presenceMode = getMotionPresenceMode(reducedMotion);

  const closeMobileMenu = () => {
    setMobileMenuOpen(false);
  };

  const handleMobileItemClick = () => {
    void triggerImpactHaptic('LIGHT');
    closeMobileMenu();
  };

  const handleSignOut = async () => {
    if (!signOut) {
      closeMobileMenu();
      return;
    }

    void triggerImpactHaptic('MEDIUM');

    try {
      await signOut();
      closeMobileMenu();
      router.push('/sign-in');
    } catch {
      closeMobileMenu();
    }
  };

  return (
    <>
      <motion.header
        className={cn(
          'sticky top-0 z-30 flex shrink-0 items-center gap-3 border-b border-border bg-surface px-3 safe-area-inset-top lg:px-5',
          showBrand ? 'h-14 lg:h-24' : 'glass-panel h-11 justify-between border-border/60 lg:h-12 lg:px-6',
          className,
        )}
        layout={!reducedMotion}
        transition={reducedMotion ? { duration: 0 } : { type: 'spring', stiffness: 380, damping: 32, mass: 0.9 }}
      >
        <div className={cn('flex items-center gap-2.5', showBrand && 'shrink-0')}>
          <button
            className={cn('pressable -ml-1 inline-flex items-center justify-center lg:hidden', ICON_BUTTON_CLASS)}
            onClick={() => {
              void triggerImpactHaptic('LIGHT');
              setMobileMenuOpen((current) => !current);
            }}
            aria-label={mobileMenuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={mobileMenuOpen}
            aria-controls="mobile-menu"
          >
            {mobileMenuOpen ? <X className="w-5 h-5" aria-hidden="true" /> : <Menu className="w-5 h-5" aria-hidden="true" />}
          </button>

          {onToggleSidebar ? (
            <button
              type="button"
              className="pressable -ml-1 hidden rounded-xl p-2 text-muted transition-colors hover:bg-background-light hover:text-navy lg:inline-flex"
              onClick={() => {
                void triggerImpactHaptic('LIGHT');
                onToggleSidebar();
              }}
              aria-label={sidebarCollapsed ? 'Show navigation' : 'Hide navigation'}
              aria-expanded={!sidebarCollapsed}
            >
              <Menu className="h-6 w-6" aria-hidden="true" />
            </button>
          ) : null}

          {showBrand ? (
            <Link
              href="/"
              className="pressable flex items-center transition-opacity hover:opacity-90"
              aria-label="OET with Dr Ahmed Hesham home"
              onClick={() => { void triggerImpactHaptic('LIGHT'); }}
            >
              <Image
                src="/brand/oet-with-dr-hesham-logo.png"
                alt="OET with Dr Ahmed Hesham"
                width={400}
                height={200}
                priority
                className="h-[3.25rem] w-auto object-contain lg:h-[5.5rem]"
              />
            </Link>
          ) : null}

          <AnimatePresence mode="wait" initial={false}>
            {pageTitle && !showBrand && (
              <motion.div
                key={pageTitle}
                className="hidden items-center gap-2 sm:flex"
                {...getSurfaceMotion('state', reducedMotion)}
              >
                <span className="text-muted/40">/</span>
                <span className="text-sm font-semibold text-navy">{pageTitle}</span>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {showBrand ? (
          <div className="hidden min-w-0 flex-1 justify-center px-4 md:flex lg:px-8">
            <GlobalSearch className="max-w-2xl" />
          </div>
        ) : null}

        <motion.div
          className={cn('ml-auto flex items-center justify-end gap-1.5 sm:gap-2', showBrand ? 'shrink-0' : 'flex-wrap gap-1')}
          layout={!reducedMotion}
        >
          {actions}
          {showStreakBadges && <LearnerStreakBadges className="hidden sm:flex" />}
          {showBrand ? (
            <>
              {/* Bell then theme, matching the reference header order. The tour
                  launcher moves into the account menu to keep the bar clean. */}
              <NotificationCenter triggerClassName={ICON_BUTTON_CLASS} />
              <ThemeToggle className={ICON_BUTTON_CLASS} />
              <ProfileMenu
                displayName={displayName}
                avatarUrl={authContext?.user?.avatarUrl}
                email={userSummary?.email ?? ''}
                roleLabel={ROLE_LABEL[workspaceRole ?? 'learner'] ?? 'Learner'}
                settingsHref={getWorkspaceSettingsHref(workspaceRole)}
                onSignOut={signOut ? () => { void handleSignOut(); } : undefined}
                workspaceRole={workspaceRole}
              />
            </>
          ) : (
            <>
              <TourLauncher workspaceRole={workspaceRole} />
              <ThemeToggle />
              <NotificationCenter />
              <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-[11px] font-bold text-primary ring-1 ring-primary/10 lg:h-9 lg:w-9 lg:text-sm">
                {initials}
              </div>
            </>
          )}
        </motion.div>
      </motion.header>

      <AnimatePresence mode={presenceMode}>
        {mobileMenuOpen && (
          <div className="lg:hidden fixed inset-0 z-40" role="dialog" aria-modal="true">
            <motion.button
              type="button"
              className="absolute inset-0 bg-navy/25 backdrop-blur-[2px]"
              onClick={() => {
                void triggerImpactHaptic('LIGHT');
                closeMobileMenu();
              }}
              aria-label="Close mobile menu"
              {...overlayMotionProps}
            />
            <motion.nav
              id="mobile-menu"
              aria-label="Mobile menu"
              className="glass-panel absolute left-3 right-3 top-[calc(3.25rem+env(safe-area-inset-top))] max-h-[calc(100dvh-3.25rem-var(--bottom-nav-height)-env(safe-area-inset-top)-env(safe-area-inset-bottom)-0.5rem)] overflow-hidden rounded-[1.75rem] border-border/60 shadow-[0_24px_60px_rgba(15,23,42,0.18)]"
              {...menuMotionProps}
            >
              <div className="flex max-h-[inherit] flex-col">
                <div className="border-b border-border/60 px-4 py-4">
                  <p className="text-xs font-semibold uppercase tracking-[0.24em] text-muted">Navigation</p>
                  <p className="mt-1 text-sm font-semibold text-navy">Move quickly between practice areas.</p>
                </div>

                <div className="flex-1 overflow-y-auto px-3 py-3">
                  {hasSectionedMenu ? (
                    <div className="space-y-4">
                      {visibleSectionedItems?.map((section) => (
                        <div key={section.label}>
                          <div className="mb-2 px-2 text-[11px] font-semibold uppercase tracking-[0.24em] text-muted">{section.label}</div>
                          <ul className="flex flex-col gap-1">
                            {section.items.map((item, itemIndex) => {
                              const active = sectionedActiveHref === item.href;
                              return (
                                <motion.li
                                  key={`${section.label}:${item.href}`}
                                  {...itemMotionProps}
                                  transition={{
                                    ...itemMotionProps.transition,
                                    delay: getMotionDelay(itemIndex, reducedMotion),
                                  }}
                                >
                                  <Link
                                    href={item.href}
                                    onClick={handleMobileItemClick}
                                    className={cn(
                                      'pressable flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-[13px] font-semibold',
                                      active ? 'bg-primary/12 text-primary-dark ring-1 ring-primary/15 dark:text-primary' : 'text-muted hover:bg-primary hover:text-white dark:hover:bg-primary',
                                    )}
                                    aria-current={active ? 'page' : undefined}
                                  >
                                    {item.icon}
                                    {item.label}
                                  </Link>
                                </motion.li>
                              );
                            })}
                          </ul>
                        </div>
                      ))}

                      <div className="border-t border-border/60 pt-4">
                        <div className="mb-2 px-2 text-[11px] font-semibold uppercase tracking-[0.24em] text-muted">Quick links</div>
                        <div className="grid grid-cols-2 gap-2">
                          <Link
                            href={getWorkspaceSettingsHref(workspaceRole)}
                            onClick={handleMobileItemClick}
                            className="pressable flex items-center gap-2 rounded-xl border border-border/60 bg-surface/95 px-3 py-2 text-[13px] font-semibold text-navy shadow-sm hover:bg-primary hover:text-white"
                          >
                            <Settings className="h-4 w-4" aria-hidden="true" />
                            Settings
                          </Link>
                          <a
                            href={buildSupportMailto(userSummary?.email ?? undefined)}
                            onClick={handleMobileItemClick}
                            className="pressable flex items-center gap-2 rounded-xl border border-border/60 bg-surface/95 px-3 py-2 text-[13px] font-semibold text-navy shadow-sm hover:bg-primary hover:text-white"
                          >
                            <HelpCircle className="h-4 w-4" aria-hidden="true" />
                            Help & Support
                          </a>
                        </div>
                      </div>
                    </div>
                  ) : (
                    <ul className="flex flex-col gap-1">
                      {items.map((item, index) => {
                        const active = flatActiveHref === item.href;
                        return (
                          <motion.li
                            key={`${index}:${item.href}`}
                            {...itemMotionProps}
                            transition={{
                              ...itemMotionProps.transition,
                              delay: getMotionDelay(index, reducedMotion),
                            }}
                          >
                            <Link
                              href={item.href}
                              onClick={handleMobileItemClick}
                              className={cn(
                                'pressable flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-[13px] font-semibold',
                                active ? 'bg-primary/12 text-primary-dark ring-1 ring-primary/15 dark:text-primary' : 'text-muted hover:bg-primary hover:text-white dark:hover:bg-primary',
                              )}
                              aria-current={active ? 'page' : undefined}
                            >
                              {item.icon}
                              {item.label}
                            </Link>
                          </motion.li>
                        );
                      })}
                    </ul>
                  )}

                  {signOut && (
                    <div className={cn('pt-4', hasSectionedMenu && 'mt-4 border-t border-border/60')}>
                      <button
                        type="button"
                        onClick={() => {
                          void handleSignOut();
                        }}
                        className="pressable flex w-full items-center justify-center gap-2 rounded-xl border border-border/60 bg-surface/95 px-3 py-2 text-[13px] font-semibold text-navy shadow-sm hover:bg-primary hover:text-white"
                      >
                        <LogOut className="h-4 w-4" aria-hidden="true" />
                        Sign out
                      </button>
                    </div>
                  )}
                </div>
              </div>
            </motion.nav>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
