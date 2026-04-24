'use client';

import { ThemeProvider as NextThemesProvider } from 'next-themes';
import type { ReactNode } from 'react';

export function ThemeProvider({ children, nonce }: { children: ReactNode; nonce?: string }) {
  return (
    <NextThemesProvider attribute="class" defaultTheme="light" enableSystem nonce={nonce}>
      {children}
    </NextThemesProvider>
  );
}
