import type { ReactNode } from 'react';
import { LearnerShellLayout } from '@/components/layout/learner-dashboard-shell';

// The one place the learner shell mounts; resolveLearnerChrome picks each route's chrome.
export default function LearnerLayout({ children }: { children: ReactNode }) {
  return <LearnerShellLayout>{children}</LearnerShellLayout>;
}
