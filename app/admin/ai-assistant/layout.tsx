import type { JSX, ReactNode } from 'react';

export default function AiAssistantAdminLayout({ children }: { children: ReactNode }): JSX.Element {
  return <section className="space-y-4">{children}</section>;
}
