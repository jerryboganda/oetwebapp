'use client';

import { cn } from '@/lib/utils';
import { BriefcaseMedical, Menu, X } from 'lucide-react';
import Link from 'next/link';
import { useState } from 'react';
import { mainNavItems, type NavItem, type ShellUserSummary } from './sidebar';
import { usePathname } from 'next/navigation';
import { NotificationCenter } from './notification-center';

interface TopNavProps {
  pageTitle?: string;
  className?: string;
  actions?: React.ReactNode;
  items?: NavItem[];
  userSummary?: ShellUserSummary;
}

export function TopNav({ pageTitle, className, actions, items = mainNavItems, userSummary }: TopNavProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const pathname = usePathname();
  const displayName = userSummary?.displayName?.trim() || 'User';
  const initials = displayName.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase();

  return (
    <>
      <header
        className={cn(
          'min-h-16 safe-area-inset-top bg-surface border-b border-border flex items-center justify-between px-4 lg:px-6 shrink-0 sticky top-0 z-30 shadow-sm',
          className,
        )}
      >
        <div className="flex items-center gap-3">
          {/* Mobile menu toggle */}
          <button
            className="lg:hidden p-2 text-muted hover:text-navy transition-colors rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            aria-label={mobileMenuOpen ? 'Close menu' : 'Open menu'}
            aria-expanded={mobileMenuOpen}
            aria-controls="mobile-menu"
          >
            {mobileMenuOpen ? <X className="w-5 h-5" aria-hidden="true" /> : <Menu className="w-5 h-5" aria-hidden="true" />}
          </button>

          {/* Logo */}
          <Link href="/" className="flex items-center gap-2 text-navy" aria-label="OET Prep home">
            <div className="w-8 h-8 flex items-center justify-center bg-primary text-white rounded">
              <BriefcaseMedical className="w-5 h-5" aria-hidden="true" />
            </div>
            <h1 className="text-lg font-bold tracking-tight hidden sm:block">OET Prep</h1>
          </Link>

          {/* Page title */}
          {pageTitle && (
            <>
              <span className="text-gray-300 hidden sm:inline">/</span>
              <span className="text-sm font-semibold text-navy hidden sm:inline">{pageTitle}</span>
            </>
          )}
        </div>

        <div className="flex items-center gap-3">
          {actions}
          <NotificationCenter />
          <div className="w-9 h-9 rounded-full bg-primary/10 flex items-center justify-center text-primary font-bold text-sm">
            {initials}
          </div>
        </div>
      </header>

      {/* Mobile slide-out menu */}
      {mobileMenuOpen && (
        <div className="lg:hidden fixed inset-0 z-40" role="dialog" aria-modal="true">
          <div className="fixed inset-0 bg-black/40" onClick={() => setMobileMenuOpen(false)} aria-hidden="true" />
          <nav id="mobile-menu" className="fixed top-16 left-0 bottom-0 w-64 bg-surface shadow-xl overflow-y-auto animate-in slide-in-from-left duration-200" aria-label="Mobile menu">
            <ul className="flex flex-col gap-0.5 p-3">
              {items.map((item) => {
                const active = item.href === '/' ? pathname === '/' : pathname?.startsWith(item.matchPrefix ?? item.href) ?? false;
                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={() => setMobileMenuOpen(false)}
                      className={cn(
                        'flex items-center gap-3 px-3 py-2.5 rounded text-sm font-semibold transition-colors',
                        active ? 'bg-primary/10 text-primary' : 'text-muted hover:text-navy hover:bg-gray-50',
                      )}
                    >
                      {item.icon}
                      {item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </nav>
        </div>
      )}
    </>
  );
}
