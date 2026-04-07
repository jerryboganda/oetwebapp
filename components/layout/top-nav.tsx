'use client';

import { cn } from '@/lib/utils';
import { BriefcaseMedical, Menu, X } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { mainNavItems, type NavItem, type ShellUserSummary } from './sidebar';
import { usePathname } from 'next/navigation';
import { NotificationCenter } from './notification-center';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';

interface TopNavProps {
  pageTitle?: string;
  className?: string;
  actions?: React.ReactNode;
  items?: NavItem[];
  userSummary?: ShellUserSummary;
}

export function TopNav({ pageTitle, className, actions, items = mainNavItems, userSummary }: TopNavProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const pathname = usePathname() ?? '/';
  const prefersReducedMotion = useReducedMotion() ?? false;
  const displayName = userSummary?.displayName?.trim() || 'User';
  const initials = displayName.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase();

  const menuMotionProps = prefersReducedMotion
    ? { initial: false, animate: { opacity: 1 }, exit: { opacity: 0 }, transition: { duration: 0 } }
    : {
        initial: { opacity: 0, x: -20 },
        animate: { opacity: 1, x: 0 },
        exit: { opacity: 0, x: -20 },
        transition: { duration: 0.22, ease: [0.22, 1, 0.36, 1] as const },
      };

  return (
    <>
      <header
        className={cn(
          'glass-panel sticky top-0 z-30 flex min-h-16 shrink-0 items-center justify-between border-b border-border/60 px-4 safe-area-inset-top lg:px-6',
          className,
        )}
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
              <span className="text-[10px] font-semibold uppercase tracking-[0.24em] text-muted">Learner workspace</span>
            </div>
          </Link>

          {pageTitle && (
            <>
              <span className="hidden text-gray-300 sm:inline">/</span>
              <span className="hidden text-sm font-semibold text-navy sm:inline">{pageTitle}</span>
            </>
          )}
        </div>

        <div className="flex items-center gap-3">
          {actions}
          <NotificationCenter />
          <div className="flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-sm font-bold text-primary ring-1 ring-primary/10">
            {initials}
          </div>
        </div>
      </header>

      <AnimatePresence>
        {mobileMenuOpen && (
          <div className="lg:hidden fixed inset-0 z-40" role="dialog" aria-modal="true">
            <motion.button
              type="button"
              className="absolute inset-0 bg-slate-950/42 backdrop-blur-[2px]"
              onClick={() => {
                void triggerImpactHaptic('LIGHT');
                setMobileMenuOpen(false);
              }}
              aria-label="Close mobile menu"
              {...(prefersReducedMotion ? { initial: false, animate: { opacity: 1 }, exit: { opacity: 0 } } : { initial: { opacity: 0 }, animate: { opacity: 1 }, exit: { opacity: 0 } })}
            />
            <motion.nav
              id="mobile-menu"
              aria-label="Mobile menu"
              className="glass-panel absolute left-3 right-3 top-[calc(4rem+env(safe-area-inset-top))] overflow-hidden rounded-[1.75rem] border-border/60 shadow-[0_24px_60px_rgba(15,23,42,0.18)]"
              {...menuMotionProps}
            >
              <div className="border-b border-border/60 px-4 py-4">
                <p className="text-xs font-semibold uppercase tracking-[0.24em] text-muted">Navigation</p>
                <p className="mt-1 text-sm font-semibold text-navy">Move quickly between practice areas.</p>
              </div>
              <ul className="flex flex-col gap-1 p-3">
                {items.map((item) => {
                  const active = item.href === '/' ? pathname === '/' : pathname.startsWith(item.matchPrefix ?? item.href);
                  return (
                    <li key={item.href}>
                      <Link
                        href={item.href}
                        onClick={() => {
                          void triggerImpactHaptic('LIGHT');
                          setMobileMenuOpen(false);
                        }}
                        className={cn(
                          'pressable flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold',
                          active ? 'bg-primary/12 text-primary ring-1 ring-primary/15' : 'text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5',
                        )}
                      >
                        {item.icon}
                        {item.label}
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </motion.nav>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
