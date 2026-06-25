'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BookOpen, FilePenLine, FileQuestion, Headphones, MessageSquare, Mic } from 'lucide-react';
import { cn } from '@/lib/utils';

const learnerSkillModules = [
  { href: '/writing', label: 'Writing', shortLabel: 'Writing', icon: FilePenLine, description: 'Letters and case notes' },
  { href: '/speaking', label: 'Speaking', shortLabel: 'Speaking', icon: Mic, description: 'Roleplay and fluency' },
  { href: '/reading', label: 'Reading', shortLabel: 'Reading', icon: BookOpen, description: 'Parts A, B, and C' },
  { href: '/listening', label: 'Listening', shortLabel: 'Listening', icon: Headphones, description: 'Audio, notes, and review' },
  { href: '/mocks', label: 'Mocks', shortLabel: 'Mocks', icon: FileQuestion, description: 'Timed transfer practice' },
  { href: '/conversation', label: 'AI Conversation', shortLabel: 'Conversation', icon: MessageSquare, description: 'Interactive scenarios' },
] as const;

const moduleAliases: Record<string, readonly string[]> = {
  Speaking: ['SpeakingSession'],
};

function isActive(pathname: string | null, href: string) {
  if (!pathname) return false;
  return pathname === href || pathname.startsWith(`${href}/`);
}

function isModuleAllowed(label: string, enabledModules?: readonly string[]) {
  if (!enabledModules) return true;
  const allowed = new Set(enabledModules.map((module) => module.toLowerCase()));
  if (allowed.size === 0) return false;
  if (allowed.has(label.toLowerCase())) return true;
  return (moduleAliases[label] ?? []).some((alias) => allowed.has(alias.toLowerCase()));
}

export function LearnerSkillSwitcher({
  className,
  compact = false,
  title = 'Switch practice focus',
  enabledModules,
}: {
  className?: string;
  compact?: boolean;
  title?: string;
  enabledModules?: readonly string[];
}) {
  const pathname = usePathname();
  const modules = learnerSkillModules.filter((module) => isModuleAllowed(module.label, enabledModules));

  if (modules.length === 0) return null;

  return (
    <nav
      className={cn('rounded-2xl border border-border bg-surface p-3 shadow-sm', className)}
      aria-label="Skill modules"
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <p className="text-xs font-bold uppercase tracking-[0.18em] text-muted">{title}</p>
      </div>
      <div className={cn('grid gap-2', compact ? 'grid-cols-2 sm:grid-cols-4 lg:grid-cols-7' : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-4')}>
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
