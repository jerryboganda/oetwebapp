import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    testTimeout: 15000,
    setupFiles: ['./vitest.setup.ts'],
    server: {
      deps: {
        inline: [/^@testing-library\/react/, /^react-dom\/test-utils$/, /^next\//],
      },
    },
    include: ['**/__tests__/**/*.{test,spec}.{ts,tsx}', '**/*.{test,spec}.{ts,tsx}'],
    exclude: [
      '**/node_modules/**',
      'tests/e2e/**',
      'tests/a11y/**',
      '.kilo/**',
      '.claude/**',
      'OET Web App Login only screens take from here/**',
      'copilot-worktrees/**',
      // Nested git worktrees ship their own node_modules (duplicate React →
      // invalid-hook-call); never collect their test copies in the main run.
      '.claude/worktrees/**',
      '.worktrees/**',
    ],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '.'),
      'react-dom/test-utils': path.resolve(__dirname, 'tests/shims/react-dom-test-utils.ts'),
      'recharts': path.resolve(__dirname, 'tests/mocks/recharts.tsx'),
      // Native-only plugin (migration pending, not installed). Stub so test
      // collection can statically resolve the dynamic import; never invoked in jsdom.
      '@aparajita/capacitor-secure-storage': path.resolve(__dirname, 'tests/mocks/capacitor-secure-storage.ts'),
    },
  },
});
