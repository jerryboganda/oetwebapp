'use client';

import { cn } from '@/lib/utils';
import Image from 'next/image';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { useMemo } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import { getSharedLayoutId, getSurfaceMotion, getSurfaceTransition, prefersReducedMotion } from '@/lib/motion';
import type { UserRole } from '@/lib/types/auth';
import { collectFeatureFlagKeys, isFeatureFlaggedItemVisible, useFeatureFlagMap } from '@/hooks/use-feature-flag-map';
import { useEnabledModules } from '@/hooks/use-enabled-modules';
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
  LogOut,
  HelpCircle,
  Brain,
  Video,
  Lightbulb,
  BookMarked,
  MessageSquare,
  AlertTriangle,
  FolderOpen,
  Sparkles,
} from 'lucide-react';

export interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  matchPrefix?: string;
  exact?: boolean;
  featureFlag?: string;
  /**
   * Canonical PascalCase module key (see hooks/use-enabled-modules MODULE_KEYS). When set, the item
   * is hidden for learners whose plan has that admin-togglable module disabled. Fail-open otherwise.
   */
  moduleKey?: string;
}

export interface NavGroup {
  label: string;
  items: NavItem[];
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
  { href: '/materials', label: 'Materials', icon: <FolderOpen className="w-5 h-5" />, matchPrefix: '/materials' },
  { href: '/readiness', label: 'Readiness', icon: <Target className="w-5 h-5" />, matchPrefix: '/readiness' },
  { href: '/progress', label: 'Progress', icon: <TrendingUp className="w-5 h-5" />, matchPrefix: '/progress' },
  { href: '/subscriptions', label: 'Subscriptions & Packages', icon: <Sparkles className="w-5 h-5" />, matchPrefix: '/subscriptions' },
  { href: '/billing', label: 'Billing', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/billing' },
  { href: '/submissions', label: 'History', icon: <History className="w-5 h-5" />, matchPrefix: '/submissions' },
  { href: '/escalations', label: 'Escalations', icon: <AlertTriangle className="w-5 h-5" />, matchPrefix: '/escalations' },
];

// Learner workspace nav keeps the core practice modules plus Recalls,
// Progress, and Billing. Speaking is surfaced here as a primary learner
// module; the more operational surfaces (Study Plan, Readiness, History,
// Escalations) remain addressable by URL and visible to admins/tutors via
// `mainNavItems`, but stay out of the learner workspace nav.
export const learnerMainNavItems: NavItem[] = [
  { href: '/', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/' },
  { href: '/listening', label: 'Listening', icon: <Headphones className="w-5 h-5" />, matchPrefix: '/listening' },
  { href: '/reading', label: 'Reading', icon: <BookOpen className="w-5 h-5" />, matchPrefix: '/reading' },
  { href: '/writing', label: 'Writing', icon: <FilePenLine className="w-5 h-5" />, matchPrefix: '/writing' },
  { href: '/speaking', label: 'Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/speaking' },
  { href: '/mocks', label: 'Mocks', icon: <FileQuestion className="w-5 h-5" />, matchPrefix: '/mocks', moduleKey: 'Mocks' },
  { href: '/recalls', label: 'Recalls', icon: <Brain className="w-5 h-5" />, matchPrefix: '/recalls', moduleKey: 'Recalls' },
  { href: '/materials', label: 'Materials', icon: <FolderOpen className="w-5 h-5" />, matchPrefix: '/materials', moduleKey: 'MaterialsLibrary' },
  { href: '/videos', label: 'Videos', icon: <Video className="w-5 h-5" />, matchPrefix: '/videos', featureFlag: 'video_library', moduleKey: 'VideoLibrary' },
  { href: '/progress', label: 'Progress', icon: <TrendingUp className="w-5 h-5" />, matchPrefix: '/progress' },
  { href: '/subscriptions', label: 'Subscriptions & Packages', icon: <Sparkles className="w-5 h-5" />, matchPrefix: '/subscriptions' },
  { href: '/billing', label: 'Billing', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/billing' },
];

export const learnNavItems: NavItem[] = [
  { href: '/grammar', label: 'Grammar', icon: <BookMarked className="w-5 h-5" />, matchPrefix: '/grammar' },
  { href: '/classes', label: 'Live Classes', icon: <CalendarCheck className="w-5 h-5" />, matchPrefix: '/classes' },
  { href: '/videos', label: 'Video Library', icon: <Video className="w-5 h-5" />, matchPrefix: '/videos', featureFlag: 'video_library' },
  { href: '/strategies', label: 'Strategies', icon: <Lightbulb className="w-5 h-5" />, matchPrefix: '/strategies', featureFlag: 'strategy_guides' },
  // Per PRD Phase 2 §2 the dedicated /pronunciation tab is removed and the
  // pronunciation engine is merged into Recalls — clicking a recall word plays
  // the audio (gated behind an active subscription server-side).
  { href: '/recalls', label: 'Recalls', icon: <Brain className="w-5 h-5" />, matchPrefix: '/recalls' },
  { href: '/conversation', label: 'AI Conversation', icon: <MessageSquare className="w-5 h-5" />, matchPrefix: '/conversation' },
];

export const mobileNavItems: NavItem[] = [
  mainNavItems[0], // Dashboard
  mainNavItems[1], // Study Plan
  mainNavItems[2], // Writing
  mainNavItems[3], // Speaking
  mainNavItems[4], // Reading
  mainNavItems[5], // Listening
  mainNavItems[6], // Mocks
];

// Curated 7-item learner bottom-nav (fits grid-cols-7 without overflow).
// Videos takes the 7th slot (mobile is a primary Video Library surface —
// playback is native-app-only); Materials, Progress and Billing remain
// reachable via the desktop sidebar and Settings page.
export const learnerMobileNavItems: NavItem[] = [
  learnerMainNavItems[0], // Dashboard
  learnerMainNavItems[1], // Listening
  learnerMainNavItems[2], // Reading
  learnerMainNavItems[3], // Writing
  learnerMainNavItems[4], // Speaking
  learnerMainNavItems[5], // Mocks
  learnerMainNavItems[8], // Videos
];

function isActive(pathname: string | null, item: NavItem): boolean {
  if (!pathname) return false;
  if (item.exact) return pathname === item.href;
  if (item.href === '/') return pathname === '/';
  return pathname.startsWith(item.matchPrefix ?? item.href);
}

function getActiveItemHref(pathname: string | null, items: NavItem[]): string | null {
  const activeItems = items.filter((item) => isActive(pathname, item));
  if (activeItems.length === 0) return null;

  return [...activeItems].sort((firstItem, secondItem) => {
    const firstPrefix = firstItem.matchPrefix ?? firstItem.href;
    const secondPrefix = secondItem.matchPrefix ?? secondItem.href;
    return secondPrefix.length - firstPrefix.length;
  })[0].href;
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
  const activeHref = getActiveItemHref(pathname, items);

  return (
    <motion.div className="mb-4" layout={!reducedMotion}>
      <div className="mb-2 px-2 text-xs font-semibold uppercase tracking-[0.22em] text-muted">{label}</div>
      <motion.ul className="flex flex-col gap-1" layout={!reducedMotion}>
        {items.map((item, index) => {
          const active = item.href === activeHref;
          return (
            <motion.li key={`${label}:${index}:${item.href}`} layout={!reducedMotion}>
              <Link
                href={item.href}
                onClick={() => {
                  void triggerImpactHaptic('LIGHT');
                }}
                className={cn(
                  'pressable group relative flex items-center gap-3 overflow-hidden rounded-2xl px-4 py-2.5 text-sm font-semibold',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                  active ? 'text-primary-dark dark:text-primary' : 'text-muted hover:bg-primary hover:text-white dark:hover:bg-primary',
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
                <span className={cn('relative z-10 flex items-center justify-center', active ? 'text-primary-dark dark:text-primary' : 'text-muted group-hover:text-white')}>
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
  items,
  groups,
  userSummary,
  workspaceRole,
}: {
  className?: string;
  items?: NavItem[];
  groups?: NavGroup[];
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
  // Default the nav list based on workspace: learners get the trimmed 7-item
  // list per the 2026-05-27 OET sample-test alignment; admins/tutors keep the
  // full 12-item operational list.
  const resolvedItems = items ?? (isLearnerWorkspace ? learnerMainNavItems : mainNavItems);
  const workspaceLabel =
    activeWorkspaceRole === 'expert'
      ? 'Tutor workspace'
      : activeWorkspaceRole === 'admin'
        ? 'Admin workspace'
        : 'Learner workspace';
  const displayName = userSummary?.displayName ?? user?.displayName ?? 'User';
  const initials = displayName.split(' ').map((n) => n[0]).join('').slice(0, 2).toUpperCase();
  const email = userSummary?.email ?? user?.email ?? '';
  const navFeatureKeys = useMemo(
    () => collectFeatureFlagKeys([...learnNavItems, ...resolvedItems]),
    [resolvedItems],
  );
  const learnerFeatureFlags = useFeatureFlagMap(navFeatureKeys, isLearnerWorkspace);
  const { isModuleEnabled, modules: enabledModules } = useEnabledModules(isLearnerWorkspace);
  const enabledModulesKey = enabledModules.join('|');
  const visibleLearnNavItems = useMemo(
    () => learnNavItems.filter(
      (item) => isFeatureFlaggedItemVisible(item, learnerFeatureFlags, isLearnerWorkspace) && isModuleEnabled(item.moduleKey),
    ),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [isLearnerWorkspace, learnerFeatureFlags, enabledModulesKey],
  );
  const visibleMainItems = useMemo(
    () => resolvedItems.filter(
      (item) => isFeatureFlaggedItemVisible(item, learnerFeatureFlags, isLearnerWorkspace) && isModuleEnabled(item.moduleKey),
    ),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [isLearnerWorkspace, learnerFeatureFlags, resolvedItems, enabledModulesKey],
  );

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
      <div className="border-b border-border/60 px-2 py-2">
        <Link
          href="/"
          className="pressable flex items-center justify-center text-navy transition-opacity hover:opacity-90"
          aria-label="OET with Dr Ahmed Hesham home"
          onClick={() => { void triggerImpactHaptic('LIGHT'); }}
        >
          <Image
            src="/brand/oet-with-dr-hesham-logo.png"
            alt="OET with Dr Ahmed Hesham"
            width={400}
            height={140}
            priority
            className="h-auto w-full max-w-[240px] object-contain"
          />
          <span className="sr-only">{workspaceLabel}</span>
        </Link>
      </div>

      <nav className="flex-1 overflow-y-auto px-4 py-5" aria-label="Main navigation" data-tour="workspace-nav">
        {groups && groups.length > 0 ? (
          groups.map((group) => (
            <NavSection
              key={group.label}
              label={group.label}
              items={group.items}
              pathname={pathname}
              reducedMotion={reducedMotion}
            />
          ))
        ) : (
          <NavSection label="Practice" items={visibleMainItems} pathname={pathname} reducedMotion={reducedMotion} />
        )}
        {/* The dedicated "Learn" group (Grammar, Live Classes, Lessons, Strategies,
            AI Conversation) is intentionally hidden from the learner workspace per the
            sample-test-alignment directive — those surfaces remain addressable by URL but
            are off the candidate's primary path. Recalls is promoted to the main Practice
            group. We keep the Learn group available to non-learner workspaces that do NOT
            provide their own custom groups (e.g. the admin panel defines its own nav). */}
        {!isLearnerWorkspace && !(groups && groups.length > 0) ? (
          <NavSection label="Learn" items={visibleLearnNavItems} pathname={pathname} reducedMotion={reducedMotion} />
        ) : null}
      </nav>

      <div className="mt-auto border-t border-border/60 bg-white/35 p-4 dark:bg-white/5">
        <ul className="mb-4 flex flex-col gap-1.5">
          <li>
            <Link
              href="/settings"
              onClick={() => { void triggerImpactHaptic('LIGHT'); }}
              className="pressable flex items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold text-muted hover:bg-primary hover:text-white dark:hover:bg-primary"
            >
              <Settings className="h-4 w-4" aria-hidden="true" />
              Settings
            </Link>
          </li>
          <li>
            <a
              href="mailto:support@edu80.app?subject=Need%20help%20with%20my%20OET%20account&body=Hello%20Support%20Team%2C%0AI%20need%20assistance%20with%20my%20OET%20account."
              onClick={() => { void triggerImpactHaptic('LIGHT'); }}
              className="pressable flex w-full items-center gap-3 rounded-2xl px-4 py-3 text-sm font-semibold text-muted hover:bg-primary hover:text-white dark:hover:bg-primary"
            >
              <HelpCircle className="h-4 w-4" aria-hidden="true" />
              Help & Support
            </a>
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
  // Only fetch the module list when this nav actually carries module-gated items (learner bottom
  // nav). The admin/tutor bottom nav has none, so this stays a no-op fetch there.
  const hasModuleItems = items.some((item) => Boolean(item.moduleKey));
  const { isModuleEnabled, modules: enabledModules } = useEnabledModules(hasModuleItems);
  const enabledModulesKey = enabledModules.join('|');
  const visibleItems = useMemo(
    () => items.filter((item) => isModuleEnabled(item.moduleKey)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [items, enabledModulesKey],
  );
  // Keep the column count in step with the visible items so hiding a module leaves no empty cells.
  // Literal class strings so Tailwind's scanner keeps them.
  const gridColsClass =
    { 3: 'grid-cols-3', 4: 'grid-cols-4', 5: 'grid-cols-5', 6: 'grid-cols-6', 7: 'grid-cols-7' }[
      Math.min(7, Math.max(3, visibleItems.length))
    ] ?? 'grid-cols-7';

  return (
    <motion.nav
      className={cn('lg:hidden fixed inset-x-2 z-40 glass-panel rounded-[1.25rem] border-border/60 px-1 py-1 shadow-[0_18px_40px_rgba(15,23,42,0.18)] keyboard-safe-floating-bottom', className)}
      aria-label="Mobile navigation"
      layout={!reducedMotion}
      {...bottomNavMotion}
    >
      <ul className={cn('grid gap-1', gridColsClass)}>
        {visibleItems.map((item, index) => {
          const active = isActive(pathname, item);
          return (
            <li key={`${index}:${item.href}`}>
              <Link
                href={item.href}
                onClick={() => {
                  void triggerImpactHaptic('LIGHT');
                }}
                className={cn(
                  'pressable relative flex min-h-12 flex-col items-center justify-center gap-0.5 overflow-hidden rounded-[0.85rem] px-1 py-0.5 text-[10px] font-semibold leading-none',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                  active ? 'text-white shadow-[0_10px_24px_rgba(124,58,237,0.28)]' : 'text-muted hover:bg-primary hover:text-white dark:hover:bg-primary',
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
                <div className={cn('relative z-10 rounded-full p-1 transition-colors [&_svg]:h-[18px] [&_svg]:w-[18px]', active ? 'bg-white/15' : 'bg-transparent')}>
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
