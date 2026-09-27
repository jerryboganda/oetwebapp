'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { History as HistoryIcon, LayoutList, Settings } from 'lucide-react';
import { cn } from '@/lib/utils';
import { OWNER_AGENT_CONSOLE_PATH } from '@/lib/owner-agent/route-scope';

type ConsoleSection = 'sessions' | 'history' | 'settings';

const LINKS: ReadonlyArray<{ id: ConsoleSection; label: string; href: string; icon: typeof HistoryIcon }> = [
  { id: 'sessions', label: 'Sessions', href: OWNER_AGENT_CONSOLE_PATH, icon: LayoutList },
  { id: 'history', label: 'History', href: `${OWNER_AGENT_CONSOLE_PATH}/history`, icon: HistoryIcon },
  { id: 'settings', label: 'Settings', href: `${OWNER_AGENT_CONSOLE_PATH}/settings`, icon: Settings },
];

/** Which console section a pathname belongs to (session pages count as Sessions). */
export function consoleSectionFor(pathname: string | null | undefined): ConsoleSection {
  const path = pathname ?? '';
  if (path === `${OWNER_AGENT_CONSOLE_PATH}/history` || path.startsWith(`${OWNER_AGENT_CONSOLE_PATH}/history/`)) return 'history';
  if (path === `${OWNER_AGENT_CONSOLE_PATH}/settings` || path.startsWith(`${OWNER_AGENT_CONSOLE_PATH}/settings/`)) return 'settings';
  return 'sessions';
}

/** Sessions / History / Settings tabs shown on every unlocked console page. */
export function ConsoleNav({ className }: { className?: string }) {
  // usePathname() can be null (e.g. outside the App Router); treat as Sessions.
  const active = consoleSectionFor(usePathname());
  return (
    <nav aria-label="Agent console" className={cn('border-b border-admin-border', className)} data-testid="console-nav">
      <ul className="-mb-px flex flex-wrap gap-1">
        {LINKS.map(({ id, label, href, icon: Icon }) => {
          const selected = active === id;
          return (
            <li key={id}>
              <Link
                href={href}
                aria-current={selected ? 'page' : undefined}
                className={cn(
                  'inline-flex items-center gap-1.5 border-b-2 px-3 py-2 text-xs font-medium transition-colors motion-reduce:transition-none',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-admin-primary focus-visible:ring-offset-1',
                  selected
                    ? 'border-admin-primary text-admin-fg-strong'
                    : 'border-transparent text-admin-fg-muted hover:border-admin-border hover:text-admin-fg-default',
                )}
              >
                <Icon className="h-3.5 w-3.5" aria-hidden="true" />
                {label}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
