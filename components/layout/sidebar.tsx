'use client';

import { cn } from '@/lib/utils';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
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
  HelpCircle
} from 'lucide-react';

export interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  matchPrefix?: string;
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
  { href: '/submissions', label: 'History', icon: <History className="w-5 h-5" />, matchPrefix: '/submissions' },
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
  if (item.href === '/') return pathname === '/';
  return pathname.startsWith(item.matchPrefix ?? item.href);
}

/* ─── Desktop Sidebar ─── */
export function Sidebar({ className, items = mainNavItems, userSummary }: { className?: string; items?: NavItem[]; userSummary?: ShellUserSummary }) {
  const pathname = usePathname();
  const { user } = useAuth();
  const displayName = userSummary?.displayName ?? user?.displayName ?? 'User';
  const initials = displayName.split(' ').map((n) => n[0]).join('').slice(0, 2).toUpperCase();
  const email = userSummary?.email ?? user?.email ?? '';

  return (
    <aside className={cn('hidden lg:flex flex-col w-64 bg-surface border-r border-border shrink-0 sticky top-0 h-screen', className)}>
      {/* Brand Header */}
      <div className="h-16 flex items-center px-6 border-b border-gray-100 shrink-0">
        <Link href="/" className="flex items-center gap-2.5 text-navy hover:opacity-90 transition-opacity">
          <div className="w-8 h-8 flex items-center justify-center bg-primary text-white rounded-lg shadow-sm">
            <BriefcaseMedical className="w-5 h-5" />
          </div>
          <span className="font-bold text-lg tracking-tight">OET Prep</span>
        </Link>
      </div>

      {/* Main Navigation */}
      <nav className="flex-1 py-5 overflow-y-auto px-4" aria-label="Main navigation">
        <div className="text-xs font-semibold text-muted uppercase tracking-wider mb-3 px-2">Menu</div>
        <ul className="flex flex-col gap-1">
          {items.map((item) => {
            const active = isActive(pathname, item);
            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  className={cn(
                    'flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-all duration-200',
                    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-1',
                    active
                      ? 'bg-primary/10 text-primary shadow-sm'
                      : 'text-muted hover:bg-gray-100 hover:text-navy',
                  )}
                  aria-current={active ? 'page' : undefined}
                >
                  <span className={cn('flex items-center justify-center', active ? 'text-primary' : 'text-gray-400')}>
                    {item.icon}
                  </span>
                  {item.label}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>

      {/* Footer Section - Profile & Settings */}
      <div className="p-4 border-t border-gray-100/80 bg-gray-50/50 mt-auto">
        <ul className="flex flex-col gap-1 mb-4">
          <li>
            <Link
              href="/settings"
              className="flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium text-muted hover:bg-gray-100 hover:text-navy transition-colors"
            >
              <Settings className="w-4 h-4 text-gray-400" />
              Settings
            </Link>
          </li>
          <li>
            <button className="w-full flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium text-muted hover:bg-gray-100 hover:text-navy transition-colors">
              <HelpCircle className="w-4 h-4 text-gray-400" />
              Help & Support
            </button>
          </li>
        </ul>

        {/* User Profile Snippet */}
        <div className="flex items-center gap-3 px-2 py-2 rounded-lg hover:bg-gray-100 cursor-pointer transition-colors group">
          <div className="w-9 h-9 rounded-full bg-primary/20 flex items-center justify-center text-primary font-bold text-sm shrink-0 border border-primary/10 group-hover:bg-primary/30 transition-colors">
            {initials}
          </div>
          <div className="flex flex-col overflow-hidden">
            <span className="text-sm font-bold text-navy truncate">{displayName}</span>
            <span className="text-xs text-muted truncate">{email}</span>
          </div>
          <LogOut className="w-4 h-4 ml-auto text-gray-400 opacity-0 group-hover:opacity-100 transition-opacity" aria-hidden="true" />
        </div>
      </div>
    </aside>
  );
}

/* ─── Mobile Bottom Nav ─── */
export function BottomNav({ className, items = mobileNavItems }: { className?: string; items?: NavItem[] }) {
  const pathname = usePathname();

  return (
    <nav
      className={cn('lg:hidden fixed bottom-0 left-0 right-0 z-40 bg-surface border-t border-border safe-area-inset-bottom shadow-[0_-4px_6px_-1px_rgba(0,0,0,0.05)]', className)}
      aria-label="Mobile navigation"
    >
      <ul className="flex items-center justify-around py-2 px-2">
        {items.map((item) => {
          const active = isActive(pathname, item);
          return (
            <li key={item.href} className="flex-1">
              <Link
                href={item.href}
                className={cn(
                  'flex flex-col items-center gap-1 py-1 rounded-lg text-[10px] font-semibold transition-colors',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                  active ? 'text-primary' : 'text-muted hover:text-navy',
                )}
                aria-current={active ? 'page' : undefined}
              >
                <div className={cn("p-1.5 rounded-full transition-colors", active ? "bg-primary/10" : "bg-transparent")}>
                  {item.icon}
                </div>
                <span>{item.label}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
