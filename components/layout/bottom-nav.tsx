'use client';

import { cn } from '@/lib/utils';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useMemo } from 'react';
import { motion, useReducedMotionConfig } from 'motion/react';
import { getSurfaceMotion, motionTokens, prefersReducedMotion } from '@/lib/motion';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { useEnabledModules } from '@/hooks/use-enabled-modules';
import { isActive, mobileNavItems, type NavItem } from './sidebar';

/** Mobile bottom navigation (hidden at lg+). Split out of sidebar.tsx. */
export function BottomNav({ className, items = mobileNavItems }: { className?: string; items?: NavItem[] }) {
  const pathname = usePathname();
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
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
                prefetch={false}
                onClick={() => {
                  void triggerImpactHaptic('LIGHT');
                }}
                className={cn(
                  'pressable relative flex min-h-12 flex-col items-center justify-center gap-0.5 overflow-hidden rounded-[0.85rem] px-1 py-0.5 text-3xs font-semibold leading-none',
                  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                  active ? 'text-white shadow-md' : 'text-muted hover-primary',
                )}
                aria-current={active ? 'page' : undefined}
              >
                {/* Always render the active pill: under reduced motion it used to be
                    skipped entirely, leaving the white active label on the glass
                    panel with no violet fill (near-invisible). Reduced motion only
                    drops the shared-layout glide. */}
                {active && (
                  <motion.span
                    aria-hidden="true"
                    className="absolute inset-0 rounded-[1rem] bg-primary"
                    layoutId={reducedMotion ? undefined : 'bottom-nav-active-pill'}
                    transition={reducedMotion ? { duration: motionTokens.duration.instant } : motionTokens.spring.layout}
                  />
                )}
                <div className={cn('relative z-10 rounded-full p-1 transition-colors [&_svg]:h-[18px] [&_svg]:w-[18px]', active ? 'bg-white/15' : 'bg-transparent')}>
                  {item.icon}
                </div>
                <span className="relative z-10 block max-w-full truncate">{item.mobileLabel ?? item.label}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </motion.nav>
  );
}
