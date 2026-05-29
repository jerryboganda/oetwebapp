'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { Bot, MessageSquare, Settings, BarChart3 } from 'lucide-react';
import { cn } from '@/lib/utils';

const navItems = [
  { href: '/admin/ai-assistant', label: 'Overview', icon: Bot, exact: true },
  { href: '/admin/ai-assistant/threads', label: 'Threads', icon: MessageSquare },
  { href: '/admin/ai-assistant/config', label: 'Configuration', icon: Settings },
  { href: '/admin/ai-assistant/analytics', label: 'Analytics', icon: BarChart3 },
];

function isActive(pathname: string | null, href: string, exact?: boolean): boolean {
  if (!pathname) return false;
  if (exact) return pathname === href;
  return pathname.startsWith(href);
}

export default function AiAssistantLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  return (
    <div className="flex flex-col gap-4">
      <nav className="flex items-center gap-1 rounded-xl border border-admin-border bg-admin-surface p-1">
        {navItems.map((item) => {
          const active = isActive(pathname, item.href, item.exact);
          const Icon = item.icon;
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                active
                  ? 'bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]'
                  : 'text-admin-text-muted hover:bg-admin-surface-raised hover:text-admin-text',
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
      {children}
    </div>
  );
}
