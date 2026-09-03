import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    pool: 'forks',
    testTimeout: 15000,
    setupFiles: ['./vitest.setup.ts'],
    include: [
      'tests/unit/m1-exam-engines.test.ts',
      'tests/unit/m1-adversarial-stress.test.tsx',
      'components/domain/mock/UnifiedMockCoordinator.test.tsx',
    ],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '.'),
      'react-dom/test-utils': path.resolve(__dirname, 'tests/shims/react-dom-test-utils.ts'),
      'recharts': path.resolve(__dirname, 'tests/mocks/recharts.tsx'),
      '@aparajita/capacitor-secure-storage': path.resolve(__dirname, 'tests/mocks/capacitor-secure-storage.ts'),
    },
  },
});
