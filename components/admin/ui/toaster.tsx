'use client';

import * as React from 'react';
import { useTheme } from 'next-themes';
import { Toaster as SonnerToaster, toast } from 'sonner';

/* ─────────────────────────────────────────────────────────────────────
 * Toaster — sonner-backed transient feedback wrapper for the OET admin DS.
 *
 * Anchors to top-right, follows the active theme (light/dark) via
 * next-themes, and uses admin design tokens for surface, border, text,
 * shadow, and radius. Provides Sonner's rich color severity + close button
 * out of the box.
 *
 * Re-exports `toast` so callers stay within the admin barrel:
 *   import { toast } from '@/components/admin/ui/toaster';
 * ───────────────────────────────────────────────────────────────────── */

type ToasterProps = React.ComponentProps<typeof SonnerToaster>;

function Toaster({ className, ...props }: ToasterProps) {
  const { resolvedTheme } = useTheme();
  const theme: ToasterProps['theme'] =
    resolvedTheme === 'dark' ? 'dark' : 'light';

  return (
    <SonnerToaster
      theme={theme}
      position="top-right"
      expand={false}
      richColors
      closeButton
      className={className}
      toastOptions={{
        classNames: {
          toast:
            'group toast group-[.toaster]:bg-admin-bg-elevated group-[.toaster]:text-admin-fg-default group-[.toaster]:border-admin-border group-[.toaster]:shadow-admin-lg group-[.toaster]:rounded-admin',
          description: 'group-[.toast]:text-admin-fg-muted',
          actionButton:
            'group-[.toast]:bg-admin-primary group-[.toast]:text-admin-primary-fg',
          cancelButton:
            'group-[.toast]:bg-admin-bg-subtle group-[.toast]:text-admin-fg-default',
        },
      }}
      {...props}
    />
  );
}
Toaster.displayName = 'Toaster';

export { Toaster, toast };
export default Toaster;
