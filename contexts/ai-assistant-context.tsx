'use client';

// Phase 1.5(a): single React context that owns ONE SignalR connection
// for the entire admin shell. Without this, every consumer of
// `useAiAssistant()` would open its own hub connection.
//
// Usage:
//   <AiAssistantProvider><AdminShell/></AiAssistantProvider>
//   const ai = useAiAssistantContext();

import { createContext, useContext, type ReactNode } from 'react';
import type { JSX } from 'react';
import { useAiAssistant, type UseAiAssistantResult } from '@/hooks/use-ai-assistant';

const AiAssistantContext = createContext<UseAiAssistantResult | null>(null);

export function AiAssistantProvider({ children }: { children: ReactNode }): JSX.Element {
  const value = useAiAssistant();
  return <AiAssistantContext.Provider value={value}>{children}</AiAssistantContext.Provider>;
}

export function useAiAssistantContext(): UseAiAssistantResult {
  const ctx = useContext(AiAssistantContext);
  if (!ctx) {
    throw new Error('useAiAssistantContext must be used inside <AiAssistantProvider>');
  }
  return ctx;
}
