'use client';

import { cn } from '@/lib/utils';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { useEffect, useState } from 'react';
import { fetchXP, fetchStreak } from '@/lib/api';
import { motion, useReducedMotion } from 'motion/react';
import { getSharedLayoutId, getSurfaceMotion, getSurfaceTransition, prefersReducedMotion } from '@/lib/motion';
import type { UserRole } from '@/lib/types/auth';
import {
  LayoutDashboard,
  FilePenLine,
  Mic,
  BookOpen,
  Headphones,
  FileQuestion,
  TrendingUp,
  Target,
  History,
  CreditCard,
  Settings,
  CalendarCheck,
  BriefcaseMedical,
  LogOut,
  HelpCircle,
  Trophy,
  Brain,
  Layers,
  Video,
  Lightbulb,
  BookMarked,
  Users,
  Flame,
  Zap,
  CalendarDays,
  GraduationCap,
  MessageSquare,
  Store,
} from 'lucide-react';

export interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  matchPrefix?: string;
  exact?: boolean;
}

export interface ShellUserSummary {
  displayName?: string | null;
  email?: string | null;
}

export const mainNavItems: NavItem[] = [
  { href: '/', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/' },
  { href: '/study-plan', label: 'Study Plan', icon: <CalendarCheck className="w-5 h-5" />, matchPrefix: '/study-plan' },
  { href: '/writing', label: 'Writing', icon: <FilePenLine className="w-5 h-5" />, matchPrefix: '/writing' },
  { href: '/speaking', label: 'Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/speaking' },
  { href: '/reading', label: 'Reading', icon: <BookOpen className="w-5 h-5" />, matchPrefix: '/reading' },
  { href: '/listening', label: 'Listening', icon: <Headphones className="w-5 h-5" />, matchPrefix: '/listening' },
  { href: '/mocks', label: 'Mocks', icon: <FileQuestion className="w-5 h-5" />, matchPrefix: '/mocks' },
  { href: '/readiness', label: 'Readiness', icon: <Target className="w-5 h-5" />, matchPrefix: '/readiness' },
  { href: '/progress', label: 'Progress', icon: <TrendingUp className="w-5 h-5" />, matchPrefix: '/progress' },
  { href: '/subscriptions', label: 'Subscriptions', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/subscriptions' },
  { href: '/submissions', label: 'History', icon: <History className="w-5 h-5" />, matchPrefix: '/submissions' },
];

export const learnNavItems: NavItem[] = [
  { href: '/grammar', label: 'Grammar', icon: <BookMarked className="w-5 h-5" />, matchPrefix: '/grammar' },
  { href: '/lessons', label: 'Video Lessons', icon: <Video className="w-5 h-5" />, matchPrefix: '/lessons' },
  { href: '/strategies', label: 'Strategies', icon: <Lightbulb className="w-5 h-5" />, matchPrefix: '/strategies' },
  { href: '/pronunciation', label: 'Pronunciation', icon: <Mic className="w-5 h-5" />, matchPrefix: '/pronunciation' },
  { href: '/vocabulary', label: 'Vocabulary', icon: <Layers className="w-5 h-5" />, matchPrefix: '/vocabulary' },
  { href: '/review', label: 'Review', icon: <Brain className="w-5 h-5" />, matchPrefix: '/review' },
  { href: '/conversation', label: 'AI Conversation', icon: <MessageSquare className="w-5 h-5" />, matchPrefix: '/conversation' },
];

export const communityNavItems: NavItem[] = [
  { href: '/community', label: 'Community', icon: <Users className="w-5 h-5" />, matchPrefix: '/community' },
  { href: '/leaderboard', label: 'Leaderboard', icon: <Trophy className="w-5 h-5" />, matchPrefix: '/leaderboard' },
  { href: '/achievements', label: 'Achievements', icon: <Zap className="w-5 h-5" />, matchPrefix: '/achievements' },
  { href: '/tutoring', label: 'Tutoring', icon: <GraduationCap className="w-5 h-5" />, matchPrefix: '/tutoring' },
  { href: '/private-speaking', label: 'Private Speaking', icon: <Video className="w-5 h-5" />, matchPrefix: '/private-speaking' },
  { href: '/exam-booking', label: 'Exam Dates', icon: <CalendarDays className="w-5 h-5" />, matchPrefix: '/exam-booking' },
  { href: '/marketplace', label: 'Marketplace', icon: <Store className="w-5 h-5" />, matchPrefix: '/marketplace' },
];

export const mobileNavItems: NavItem[] = [
  mainNavItems[0], // Dashboard
  mainNavItems[2], // Writing
  mainNavItems[3], // Speaking
  mainNavItems[4], // Reading
  mainNavItems[5], // Listening
];

function isActive(pathname: string | null, item: NavItem): boolean {
  if (!pathname) return false;
  if (item.exact) return pathname === item.href;
  if (item.href === '/') return pathname === '/';
  return pathname.startsWith(item.matchPrefix ?? item.href);
}

function NavSection({
  label,
  items,
  pathname,
  reducedMotion,
}: {
  label: string;
  items: NavItem[];
  pathname: string | null;
  reducedMotion: boolean;
}) {
  return (
    <motion.div className="mb-4" layout={!reducedMotion}>
      <div className="mb-2 px-2 text-xs font-semibold uppercase tracking-[0.22em] text-muted">{label}</div>
      <motion.ul className="flex flex-col gap-1" layout={!reducedMotion}>
        {items.map((item) => {
          const active = isActive(pathname, item);
          return (
            <motion.li key={item.href} layout={!reducedMotion}>
              <Link
                href={item.href}
                onClick={() => {
                  void triggerImpactHaptic('LIGHT');
                }}
                className={cn(
                  'pressable relative flex items-center gap-3 overflow-hidden rounded-2xl px-4 py-2.5 text-sm font-semibold',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                  active ? 'text-primary-dark dark:text-primary' : 'text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5',
                )}
                aria-current={active ? 'page' : undefined}
              >
                {!reducedMotion && active && (
                  <motion.span
                    aria-hidden="true"
                    className="absolute inset-0 rounded-2xl bg-primary/12 shadow-[0_12px_28px_rgba(124,58,237,0.12)] ring-1 ring-primary/15"
                    layoutId={getSharedLayoutId('sidebar-nav-active', 'pill')}
                    transition={getSurfaceTransition('item', reducedMotion)}
                  />
                )}
                <span className={cn('relative z-10 flex items-center justify-center', active ? 'text-primary-dark dark:text-primary' : 'text-muted')}>
                  {item.icon}
                </span>
                <span className="relative z-10">{item.label}</span>
              </Link>
            </motion.li>
          );
        })}
      </motion.ul>
    </motion.div>
  );
}

/* ─── Desktop Sidebar ─── */
export function Sidebar({
  className,
  items = mainNavItems,
  userSummary,
  workspaceRole,
}: {
  className?: string;
  items?: NavItem[];
  userSummary?: ShellUserSummary;
  workspaceRole?: UserRole;
}) {
  const pathname = usePathname();
  const router = useRouter();
  const { user, signOut } = useAuth();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const activeWorkspaceRole = workspaceRole ?? user?.role;
  const isPrivilegedPath = pathname?.startsWith('/expert') || pathname?.startsWith('/admin');
  const isLearnerWorkspace = activeWorkspaceRole === 'learner' && !isPrivilegedPath;
  const workspaceLabel =
    activeWorkspaceRole === 'expert'
      ? 'Expert workspace'
      : activeWorkspaceRole === 'admin'
        ? 'Admin workspace'
        : 'Learner workspace';
  const displayName = userSummary?.displayName ?? user?.displayName ?? 'User';
  const initials = displayName.split(' ').map((n) => n[0]).join('').slice(0, 2).toUpperCase();
  const email = userSummary?.email ?? user?.email ?? '';
  const [streak, setStreak] = useState<number | null>(null);
  const [level, setLevel] = useState<number | null>(null);
  const visibleStreak = isLearnerWorkspace ? streak : null;
  const visibleLevel = isLearnerWorkspace ? level : null;

  useEffect(() => {
    if (!isLearnerWorkspace) {
      return;
    }

    Promise.allSettled([fetchStreak(), fetchXP()]).then(([streakR, xpR]) => {
      if (streakR.status === 'fulfilled') setStreak((streakR.value as { currentStreak: number }).currentStreak);
      if (xpR.status === 'fulfilled') setLevel((xpR.value as { level: number }).level);
    });
  }, [isLearnerWorkspace]);

  const handleSignOut = async () => {
    await signOut();
    router.push('/sign-in');
  };

  const sidebarMotion = getSurfaceMotion('section', reducedMotion);

  return (
    <motion.aside
      className={cn('glass-panel hidden h-screen shrink-0 flex-col sticky top-0 w-72 overflow-hidden border-r border-border/60 bg-surface/90 lg:flex', className)}
      layout={!reducedMotion}
      {...sidebarMotion}
    >
      <div className="border-b border-border/60 px-6 py-5">
        <Link
          href="/"
          className="pressable flex items-center gap-3 text-navy transition-opacity hover:opacity-90"
          onClick={() => { void triggerImpactHaptic('LIGHT'); }}
        >
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary text-white shadow-lg shadow-primary/20">
            <BriefcaseMedical className="h-5 w-5" />
          </div>
          <div className="flex flex-col">
            <span className="font-display text-lg font-semibold tracking-tight">OET Prep</span>
            <span className="text-xs font-medium uppercase tracking-[0.24em] text-muted">{workspaceLabel}</span>
          </div>
        </Link>
      </div>

      <nav className="flex-1 overflow-y-auto px-4 py-5" aria-label="Main navigation">
        <NavSection label="Practice" items={items} pathname={pathname} reducedMotion={reducedMotion} />
        {isLearnerWorkspace ? (
          <>
            <NavSection label="Learn" items={learnNavItems} pathname={pathname} reducedMotion={reducedMotion} />
            <NavSection label="Community" items={communityNavItems} pathname={pathname} reducedMotion={reducedMotion} />
          </>
        ) : null}
      </nav>

      <div className="mt-auto border-t border-border/60 bg-white/35 p-4 dark:bg-white/5">
        {/* Streak + Level badges */}
        {(visibleStreak !== null || visibleLevel !== null) && (
          <div className="flex items-center gap-2 mb-3 px-1">
            {visibleStreak !== null && (
              <Link
                href="/achievements"
                className="flex items-center gap-1 rounded-full bg-orange-500 px-2.5 py-1 text-xs font-bold text-white shadow-sm transition-colors hover:bg-orange-600 dark:bg-orange-600 dark:text-white dark:hover:bg-orange-500"
              >
                <Flame className="w-3.5 h-3.5" />
                {visibleStreak}d
              </Link>
            )}
            {visibleLevel !== null && (
              <Link
                href="/achievements"
                className="flex items-center gap-1 rounded-full bg-amber-500 px-2.5 py-1 text-xs font-bold text-white shadow-sm transition-colors hover:bg-amber-600 dark:bg-amber-600 dark:text-white dark:hover:bg-amber-500"
              >
                <Zap className="w-3.5 h-3.5" />
                Lv.{visibleLevel}
              </Link>
            )}
          </div>
        )}

        <ul className="mb-4 flex flex-col gap-1.5">
          <li>
            <Link
              href="/settings"
              onClick={() => { void triggerImpactHaptic('LIGHT'); }}
              className="pressable flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5"
            >
              <Settings className="h-4 w-4" />
              Settings
            </Link>
          </li>
          <li>
            <button
              type="button"
              onClick={() => { void triggerImpactHaptic('LIGHT'); }}
              className="pressable flex w-full items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5"
            >
              <HelpCircle className="h-4 w-4" />
              Help & Support
            </button>
          </li>
        </ul>

        <button
          type="button"
          className="pressable flex w-full items-center gap-3 rounded-[1.25rem] border border-border/60 bg-surface/90 px-3 py-3 text-left shadow-sm"
          onClick={() => {
            void triggerImpactHaptic('MEDIUM');
            void handleSignOut();
          }}
        >
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-sm font-bold text-primary ring-1 ring-primary/10">
            {initials}
          </div>
          <div className="flex flex-col overflow-hidden">
            <span className="text-sm font-bold text-navy truncate">{displayName}</span>
            <span className="text-xs text-muted truncate">{email}</span>
          </div>
          <LogOut className="ml-auto h-4 w-4 text-muted" aria-hidden="true" />
        </button>
      </div>
    </motion.aside>
  );
}

/* ─── Mobile Bottom Nav ─── */
export function BottomNav({ className, items = mobileNavItems }: { className?: string; items?: NavItem[] }) {
  const pathname = usePathname();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const bottomNavMotion = getSurfaceMotion('overlay', reducedMotion);

  return (
    <motion.nav
      className={cn('lg:hidden fixed inset-x-3 z-40 glass-panel rounded-[1.5rem] border-border/60 px-1.5 py-1.5 shadow-[0_18px_40px_rgba(15,23,42,0.18)] keyboard-safe-floating-bottom', className)}
      aria-label="Mobile navigation"
      layout={!reducedMotion}
      {...bottomNavMotion}
    >
      <ul className="grid grid-cols-5 gap-1">
        {items.map((item) => {
          const active = isActive(pathname, item);
          return (
            <li key={item.href}>
              <Link
                href={item.href}
                onClick={() => {
                  void triggerImpactHaptic('LIGHT');
                }}
                className={cn(
                  'pressable relative flex min-h-14 flex-col items-center justify-center gap-1 overflow-hidden rounded-[1rem] px-1 py-1 text-[10px] font-semibold',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                  active ? 'text-white shadow-[0_10px_24px_rgba(124,58,237,0.28)]' : 'text-muted hover:bg-white/70 hover:text-navy dark:hover:bg-white/5',
                )}
                aria-current={active ? 'page' : undefined}
              >
                {!reducedMotion && active && (
                  <motion.span
                    aria-hidden="true"
                    className="absolute inset-0 rounded-[1rem] bg-primary"
                    layoutId="bottom-nav-active-pill"
                    transition={getSurfaceTransition('item', reducedMotion)}
                  />
                )}
                <div className={cn('relative z-10 rounded-full p-1.5 transition-colors', active ? 'bg-white/15' : 'bg-transparent')}>
                  {item.icon}
                </div>
                <span className="relative z-10">{item.label}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </motion.nav>
  );
}
