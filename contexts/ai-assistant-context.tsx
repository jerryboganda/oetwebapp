'use client';

/**
 * AI Assistant global context provider.
 * Wraps the app to provide assistant state, connection, and UI visibility control.
 *
 * The SignalR hub is a long-polling connection plus two REST reads (threads,
 * model catalogue). It is opened lazily: nothing connects until a surface that
 * actually shows the assistant (the floating panel, the full-page companion)
 * calls `activate()`. Before this, every learner, expert and admin opened the
 * hub on every page load, even when the assistant feature was switched off.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
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
  /**
   * Opens the hub connection. Idempotent and safe to call on every mount of a
   * surface that renders the assistant; it stays active until sign-out.
   */
  activate: () => void;
}

const AiAssistantContext = createContext<AiAssistantContextValue | null>(null);

// ─── Provider ───────────────────────────────────────────────────────────────

export function AiAssistantProvider({ children }: { children: ReactNode }) {
  const { session, user } = useAuth();
  const [isOpen, setIsOpen] = useState(false);
  // Keyed by user so a different account on the same tab never inherits the
  // previous account's open hub.
  const userId = user?.userId ?? null;
  const [activatedFor, setActivatedFor] = useState<string | null>(null);
  const activated = userId !== null && activatedFor === userId;

  const token = session?.accessToken ?? null;
  const userRole = user?.role ?? null;
  const hasAccess = canAccessAssistant(userRole);

  // Only connect when the user has access AND a surface asked for the hub.
  const assistant = useAiAssistant(
    { token: hasAccess ? token : null, autoConnect: activated },
    userRole,
  );

  useEffect(() => {
    if (userId === null) setActivatedFor(null);
  }, [userId]);

  const activate = useCallback(() => {
    if (userId !== null) setActivatedFor(userId);
  }, [userId]);

  const toggle = useCallback(() => {
    activate();
    setIsOpen((v) => !v);
  }, [activate]);
  const open = useCallback(() => {
    activate();
    setIsOpen(true);
  }, [activate]);
  const close = useCallback(() => setIsOpen(false), []);

  const value = useMemo<AiAssistantContextValue>(
    () => ({
      ...assistant,
      isOpen,
      toggle,
      open,
      close,
      hasAccess,
      activate,
    }),
    [assistant, isOpen, toggle, open, close, hasAccess, activate],
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
