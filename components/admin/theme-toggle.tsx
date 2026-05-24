'use client';

/**
 * Admin Theme Toggle — Light / Dark / System
 *
 * Foundation-layer component: ships without `@radix-ui/react-dropdown-menu`
 * so it works on day 1, before the shadcn primitives in
 * `scripts/install-admin-deps.sh` are installed. When the dropdown package
 * lands, this can be re-implemented on top of it — the public API
 * (no props, `data-theme-toggle` test id) stays stable.
 *
 * Key behaviors
 * - SSR-safe: avoids hydration mismatch by gating display on a `mounted`
 *   flag (per next-themes documented pattern).
 * - Three states (Light / Dark / System) per file 13 §4.8.
 * - Icon-only on small screens; icon + tooltip on hover (>=md).
 * - Closes on outside click and `Escape`.
 */

import { Monitor, Moon, Sun } from 'lucide-react';
import { useTheme } from 'next-themes';
import { useCallback, useEffect, useRef, useState } from 'react';
import { cn } from '@/lib/utils';

type ThemeOption = 'light' | 'dark' | 'system';

const OPTIONS: ReadonlyArray<{ value: ThemeOption; label: string; Icon: typeof Sun }> = [
  { value: 'light', label: 'Light', Icon: Sun },
  { value: 'dark', label: 'Dark', Icon: Moon },
  { value: 'system', label: 'System', Icon: Monitor },
];

export function ThemeToggle() {
  const { theme, setTheme, resolvedTheme } = useTheme();
  const [mounted, setMounted] = useState(false);
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    setMounted(true);
  }, []);

  // Close on outside click + Escape
  useEffect(() => {
    if (!open) return undefined;
    const onPointerDown = (e: MouseEvent) => {
      if (!containerRef.current?.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const select = useCallback(
    (value: ThemeOption) => {
      setTheme(value);
      setOpen(false);
      triggerRef.current?.focus();
    },
    [setTheme]
  );

  // The icon shown on the trigger reflects the resolved (actual) theme
  // when the user has selected 'system'; otherwise it follows the
  // explicit preference. This matches Material 3 / GitHub Primer behavior.
  const activeForIcon = (theme === 'system' ? resolvedTheme : theme) ?? 'light';
  const TriggerIcon = activeForIcon === 'dark' ? Moon : Sun;

  // Pre-mount: render a placeholder of identical size to avoid layout shift
  // and hydration mismatch on SSR.
  if (!mounted) {
    return (
      <button
        type="button"
        aria-hidden
        tabIndex={-1}
        data-theme-toggle
        className="inline-flex h-9 w-9 items-center justify-center rounded-[var(--admin-radius-md)]"
        style={{ color: 'var(--admin-fg-muted)' }}
      >
        <Sun className="h-4 w-4" aria-hidden />
      </button>
    );
  }

  return (
    <div ref={containerRef} className="relative inline-flex">
      <button
        ref={triggerRef}
        type="button"
        data-theme-toggle
        aria-label="Toggle theme"
        aria-haspopup="menu"
        aria-expanded={open}
        title="Theme"
        onClick={() => setOpen((v) => !v)}
        className={cn(
          'group inline-flex h-9 items-center gap-2 rounded-[var(--admin-radius-md)]',
          'px-2 text-sm font-medium transition-colors',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-1'
        )}
        style={{
          color: 'var(--admin-fg-default)',
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          ['--tw-ring-color' as any]: 'var(--admin-border-focus)',
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.backgroundColor = 'var(--admin-state-hover)';
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.backgroundColor = 'transparent';
        }}
      >
        <TriggerIcon className="h-4 w-4 shrink-0" aria-hidden />
        {/* Label only visible >=md, communicates current selection on hover */}
        <span className="sr-only md:not-sr-only md:hidden md:group-hover:inline">
          {OPTIONS.find((o) => o.value === theme)?.label ?? 'System'}
        </span>
      </button>

      {open && (
        <div
          role="menu"
          aria-label="Theme options"
          className={cn(
            'absolute right-0 top-[calc(100%+6px)] z-[var(--admin-z-dropdown)]',
            'min-w-[160px] overflow-hidden rounded-[var(--admin-radius-md)]'
          )}
          style={{
            backgroundColor: 'var(--admin-bg-elevated)',
            border: '1px solid var(--admin-border-default)',
            boxShadow: 'var(--admin-shadow-lg)',
          }}
        >
          <ul className="py-1">
            {OPTIONS.map(({ value, label, Icon }) => {
              const selected = theme === value;
              return (
                <li key={value}>
                  <button
                    type="button"
                    role="menuitemradio"
                    aria-checked={selected}
                    onClick={() => select(value)}
                    className={cn(
                      'flex w-full items-center gap-2 px-3 py-1.5',
                      'text-left text-sm transition-colors'
                    )}
                    style={{
                      color: selected ? 'var(--admin-primary)' : 'var(--admin-fg-default)',
                      backgroundColor: selected ? 'var(--admin-state-selected)' : 'transparent',
                    }}
                    onMouseEnter={(e) => {
                      if (!selected) {
                        e.currentTarget.style.backgroundColor = 'var(--admin-state-hover)';
                      }
                    }}
                    onMouseLeave={(e) => {
                      if (!selected) {
                        e.currentTarget.style.backgroundColor = 'transparent';
                      }
                    }}
                  >
                    <Icon className="h-4 w-4 shrink-0" aria-hidden />
                    <span className="flex-1">{label}</span>
                    {selected && (
                      <span aria-hidden className="text-xs">
                        ✓
                      </span>
                    )}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
