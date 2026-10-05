import type { ReactNode } from 'react';
import { loadWritingMessages, resolveLocale } from '@/i18n';
import { ExtraMessagesProvider } from '@/components/providers/extra-messages-provider';
import { LearnerShellLayout } from '@/components/layout/learner-dashboard-shell';

// The one place the learner shell mounts; resolveLearnerChrome picks each route's chrome.
//
// Also the one place the Writing messages are supplied. The root layout leaves that ~50-65 KB
// bundle out of every HTML response; the learner shell (writing route titles), the Writing
// pages and the Writing components used from /submissions all live below this layout. A layout
// is rendered when its segment is entered, so this also holds after a client-side navigation
// from a page that never carried the bundle (sign-in -> dashboard -> Writing).
export default async function LearnerLayout({ children }: { children: ReactNode }) {
  const writingMessages = await loadWritingMessages(await resolveLocale());

  return (
    <ExtraMessagesProvider messages={writingMessages}>
      <LearnerShellLayout>{children}</LearnerShellLayout>
    </ExtraMessagesProvider>
  );
}
