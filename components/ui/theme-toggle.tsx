'use client';

import { Moon, Sun } from 'lucide-react';
import { useTheme } from 'next-themes';

/**
 * Always renders the real button — no `mounted` placeholder, which flashed a
 * disabled empty chip on every load. The glyph is picked by CSS (`dark:`
 * classes key off the class next-themes puts on <html>), so server and client
 * markup stay identical; `resolvedTheme` is only read inside the click
 * handler, which cannot fire before hydration anyway.
 */
export function ThemeToggle({ className = '' }: { className?: string }) {
  const { resolvedTheme, setTheme } = useTheme();
  const isDark = resolvedTheme === 'dark';

  return (
    <button
      type="button"
      onClick={() => setTheme(isDark ? 'light' : 'dark')}
      className={`inline-flex items-center justify-center rounded-lg p-2.5 text-muted hover:bg-primary/10 hover:text-primary transition-colors cursor-pointer ${className}`}
      aria-label="Toggle theme"
      title="Toggle theme"
      suppressHydrationWarning
    >
      <Sun className="size-5 dark:hidden" aria-hidden="true" />
      <Moon className="hidden size-5 dark:block" aria-hidden="true" />
    </button>
  );
}
