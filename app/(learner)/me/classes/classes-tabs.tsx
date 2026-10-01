import Link from 'next/link';
import { cn } from '@/lib/utils';

const TABS = [
  { id: 'upcoming', label: 'Upcoming', href: '/me/classes/upcoming' },
  { id: 'past', label: 'Past', href: '/me/classes/past' },
] as const;

/** Upcoming / Past switch for My Classes: route links styled as the segmented Tabs control. */
export function ClassesTabs({ active }: { active: (typeof TABS)[number]['id'] }) {
  return (
    <nav aria-label="My classes" className="flex w-full gap-1 rounded-2xl border border-border bg-background-light p-1 sm:w-fit">
      {TABS.map((tab) => {
        const current = tab.id === active;
        return (
          <Link
            key={tab.id}
            href={tab.href}
            aria-current={current ? 'page' : undefined}
            className={cn(
              'flex min-h-11 flex-1 items-center justify-center rounded-xl px-5 text-sm font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary sm:flex-none',
              current ? 'bg-surface text-primary shadow-sm' : 'hover-primary text-muted',
            )}
          >
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}
