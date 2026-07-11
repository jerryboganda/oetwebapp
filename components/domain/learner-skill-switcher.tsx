'use client';

import { useMemo } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BookOpen, FilePenLine, FileQuestion, Headphones, Repeat2, Mic } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useEnabledModules } from '@/hooks/use-enabled-modules';

const learnerSkillModules = [
  { href: '/reading', label: 'Reading', shortLabel: 'Reading', icon: BookOpen, description: 'Parts A, B, and C' },
  { href: '/listening', label: 'Listening', shortLabel: 'Listening', icon: Headphones, description: 'Audio, notes, and review' },
  { href: '/writing', label: 'Writing', shortLabel: 'Writing', icon: FilePenLine, description: 'Letters and case notes' },
  { href: '/speaking', label: 'Speaking', shortLabel: 'Speaking', icon: Mic, description: 'Roleplay and fluency' },
  { href: '/mocks', label: 'Mocks', shortLabel: 'Mocks', icon: FileQuestion, description: 'Timed transfer practice', moduleKey: 'Mocks' },
  { href: '/recalls', label: 'Recalls', shortLabel: 'Recalls', icon: Repeat2, description: 'Vocabulary and review', moduleKey: 'Recalls' },
] as const;

function isActive(pathname: string | null, href: string) {
  if (!pathname) return false;
  return pathname === href || pathname.startsWith(`${href}/`);
}

export function LearnerSkillSwitcher({
  className,
  compact = false,
  title = 'Switch practice focus',
}: {
  className?: string;
  compact?: boolean;
  title?: string;
}) {
  const pathname = usePathname();
  const { isModuleEnabled, modules: enabledModules } = useEnabledModules(true);
  const enabledModulesKey = enabledModules.join('|');
  const modules = useMemo(
    () => learnerSkillModules.filter((module) => isModuleEnabled('moduleKey' in module ? module.moduleKey : undefined)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [enabledModulesKey],
  );

  return (
    <nav
      className={cn('rounded-2xl border border-border bg-surface p-3 shadow-sm', className)}
      aria-label="Skill modules"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <p className="text-xs font-bold uppercase tracking-[0.18em] text-muted">{title}</p>
      </div>
      <div className={cn('grid gap-2', compact ? 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-6' : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3')}>
        {modules.map((module) => {
          const active = isActive(pathname, module.href);
          const Icon = module.icon;
          return (
            <Link
              key={module.href}
              href={module.href}
              aria-current={active ? 'page' : undefined}
              className={cn(
                'pressable group flex min-h-11 items-center gap-2 rounded-2xl border px-3 py-2 text-left transition-[background-color,border-color,box-shadow,color] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                active
                  ? 'border-primary/30 bg-primary/10 text-primary-dark shadow-sm'
                  : 'border-border/70 bg-background-light text-navy hover:border-primary/50 hover-primary',
              )}
            >
              <span className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-xl transition-colors', active ? 'bg-primary text-white dark:bg-violet-700' : 'bg-surface text-muted ring-1 ring-border/70 group-hover:bg-white/20 group-hover:text-white group-hover:ring-0')}>
                <Icon className="h-4 w-4" aria-hidden="true" />
              </span>
              <span className="min-w-0">
                <span className="block truncate text-sm font-bold">{compact ? module.shortLabel : module.label}</span>
                {!compact ? <span className="mt-0.5 block truncate text-xs text-muted group-hover:text-white/90">{module.description}</span> : null}
              </span>
            </Link>
          );
        })}
      </div>
    </nav>
  );
}
