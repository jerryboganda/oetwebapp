'use client';

import { getMotionDelay, getMotionPresenceMode, getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { cn } from '@/lib/utils';
import { buildSupportMailto } from '@/lib/auth/support';
import { AuthContext } from '@/contexts/auth-context';
import type { UserRole } from '@/lib/types/auth';
import { BriefcaseMedical, HelpCircle, LogOut, Menu, Settings, X } from 'lucide-react';
import Link from 'next/link';
import { type ReactNode, useContext, useState } from 'react';
import { mainNavItems, type NavItem, type ShellUserSummary } from './sidebar';
import { usePathname, useRouter } from 'next/navigation';
import { NotificationCenter } from './notification-center';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { getSurfaceTransition } from '@/lib/motion';

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
}

export function TopNav({
  pageTitle,
  className,
  actions,
  items = mainNavItems,
  sectionedItems,
  userSummary,
  workspaceRole,
}: TopNavProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const pathname = usePathname() ?? '/';
  const router = useRouter();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const displayName = userSummary?.displayName?.trim() || 'User';
  const initials = displayName.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase();
  const workspaceLabel =
    workspaceRole === 'expert'
      ? 'Expert workspace'
      : workspaceRole === 'admin'
        ? 'Admin workspace'
        : 'Learner workspace';
  const authContext = useContext(AuthContext);
  const signOut = authContext?.signOut;
  const hasSectionedMenu = Boolean(sectionedItems?.length);

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
          'glass-panel sticky top-0 z-30 flex min-h-16 shrink-0 items-center justify-between border-b border-border/60 px-4 safe-area-inset-top lg:px-6',
          className,
        )}
        layout={!reducedMotion}
        transition={reducedMotion ? { duration: 0 } : { type: 'spring', stiffness: 380, damping: 32, mass: 0.9 }}
      >
        <div className="flex items-center gap-3">
          <button
            className="touch-target pressable rounded-2xl p-2 text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5 lg:hidden"
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

          <Link
            href="/"
            className="flex items-center gap-3 text-navy"
            aria-label="OET Prep home"
            onClick={() => {
              void triggerImpactHaptic('LIGHT');
            }}
          >
            <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary text-white shadow-lg shadow-primary/20">
              <BriefcaseMedical className="w-5 h-5" aria-hidden="true" />
            </div>
            <div className="hidden flex-col sm:flex">
              <h1 className="font-display text-lg font-semibold tracking-tight">OET Prep</h1>
              <span className="text-[10px] font-semibold uppercase tracking-[0.24em] text-muted">{workspaceLabel}</span>
            </div>
          </Link>

          <AnimatePresence mode="wait" initial={false}>
            {pageTitle && (
              <motion.div
                key={pageTitle}
                className="hidden items-center gap-2 sm:flex"
                {...getSurfaceMotion('state', reducedMotion)}
              >
                <span className="text-gray-300">/</span>
                <span className="text-sm font-semibold text-navy">{pageTitle}</span>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        <motion.div className="flex flex-wrap items-center justify-end gap-2 sm:gap-3" layout={!reducedMotion}>
          {actions}
          <NotificationCenter />
          <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-sm font-bold text-primary ring-1 ring-primary/10">
            {initials}
          </div>
        </motion.div>
      </motion.header>

      <AnimatePresence mode={presenceMode}>
        {mobileMenuOpen && (
          <div className="lg:hidden fixed inset-0 z-40" role="dialog" aria-modal="true">
            <motion.button
              type="button"
              className="absolute inset-0 bg-slate-950/42 backdrop-blur-[2px]"
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
              className="glass-panel absolute left-3 right-3 top-[calc(4rem+env(safe-area-inset-top))] max-h-[calc(100dvh-5.5rem-env(safe-area-inset-top))] overflow-hidden rounded-[1.75rem] border-border/60 shadow-[0_24px_60px_rgba(15,23,42,0.18)]"
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
                      {sectionedItems?.map((section) => (
                        <div key={section.label}>
                          <div className="mb-2 px-2 text-[11px] font-semibold uppercase tracking-[0.24em] text-muted">{section.label}</div>
                          <ul className="flex flex-col gap-1">
                            {section.items.map((item, itemIndex) => {
                              const active = item.href === '/' ? pathname === '/' : pathname.startsWith(item.matchPrefix ?? item.href);
                              return (
                                <motion.li
                                  key={item.href}
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
                                      'pressable flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold',
                                      active ? 'bg-primary/12 text-primary-dark ring-1 ring-primary/15 dark:text-primary' : 'text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5',
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
                            href="/settings"
                            onClick={handleMobileItemClick}
                            className="pressable flex items-center gap-3 rounded-2xl border border-border/60 bg-surface/95 px-4 py-3 text-sm font-semibold text-navy shadow-sm hover:bg-white"
                          >
                            <Settings className="h-4 w-4" aria-hidden="true" />
                            Settings
                          </Link>
                          <a
                            href={buildSupportMailto(userSummary?.email ?? undefined)}
                            onClick={handleMobileItemClick}
                            className="pressable flex items-center gap-3 rounded-2xl border border-border/60 bg-surface/95 px-4 py-3 text-sm font-semibold text-navy shadow-sm hover:bg-white"
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
                        const active = item.href === '/' ? pathname === '/' : pathname.startsWith(item.matchPrefix ?? item.href);
                        return (
                          <motion.li
                            key={item.href}
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
                                'pressable flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold',
                                active ? 'bg-primary/12 text-primary-dark ring-1 ring-primary/15 dark:text-primary' : 'text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5',
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
                        className="pressable flex w-full items-center justify-center gap-3 rounded-2xl border border-border/60 bg-surface/95 px-4 py-3 text-sm font-semibold text-navy shadow-sm hover:bg-white"
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
