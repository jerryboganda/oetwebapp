'use client';

/**
 * AI Assistant global context provider.
 * Wraps the app to provide assistant state, connection, and UI visibility control.
 */

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { useAuth } from '@/contexts/auth-context';
import { useAiAssistant, type UseAiAssistantReturn } from '@/hooks/use-ai-assistant';
import { canAccessAssistant } from '@/lib/ai-assistant/permissions';

// ─── Context Value ──────────────────────────────────────────────────────────

export interface AiAssistantContextValue extends UseAiAssistantReturn {
  /** Whether the chat panel is visible */
  isOpen: boolean;
  /** Toggle panel visibility */
  toggle: () => void;
  /** Open the panel */
  open: () => void;
  /** Close the panel */
  close: () => void;
  /** Whether the user has access to the assistant */
  hasAccess: boolean;
}

const AiAssistantContext = createContext<AiAssistantContextValue | null>(null);

// ─── Provider ───────────────────────────────────────────────────────────────

export function AiAssistantProvider({ children }: { children: ReactNode }) {
  const { session, user } = useAuth();
  const [isOpen, setIsOpen] = useState(false);

  const token = session?.accessToken ?? null;
  const userRole = user?.role ?? null;
  const hasAccess = canAccessAssistant(userRole);

  // Only connect when user has access
  const assistant = useAiAssistant(hasAccess ? token : null, userRole);

  const toggle = useCallback(() => setIsOpen((v) => !v), []);
  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);

  const value = useMemo<AiAssistantContextValue>(
    () => ({
      ...assistant,
      isOpen,
      toggle,
      open,
      close,
      hasAccess,
    }),
    [assistant, isOpen, toggle, open, close, hasAccess],
  );

  return (
    <AiAssistantContext.Provider value={value}>
      {children}
    </AiAssistantContext.Provider>
  );
}

// ─── Hook ───────────────────────────────────────────────────────────────────

export function useAiAssistantContext(): AiAssistantContextValue {
  const context = useContext(AiAssistantContext);
  if (!context) {
    throw new Error('useAiAssistantContext must be used within AiAssistantProvider');
  }
  return context;
}
