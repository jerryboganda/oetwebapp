'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { Bot, Loader2, PowerOff, ShieldAlert } from 'lucide-react';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { useOwnerAgent, type UseOwnerAgentReturn } from '@/hooks/use-owner-agent';
import { isOwnerAgentConsolePath } from '@/lib/owner-agent/route-scope';
import { ConsoleNav } from './ConsoleNav';
import { UnlockGate } from './UnlockGate';

const OwnerAgentConsoleContext = createContext<UseOwnerAgentReturn | null>(null);

/** Console-wide state (owner, unlock, status, sessions, lease) for the console pages. */
export function useOwnerAgentConsole(): UseOwnerAgentReturn {
  const value = useContext(OwnerAgentConsoleContext);
  if (!value) throw new Error('useOwnerAgentConsole must be used inside <OwnerAgentConsoleShell>.');
  return value;
}

function loadedOutsideConsole(): boolean {
  if (typeof window === 'undefined' || typeof performance === 'undefined') return false;
  if (typeof performance.getEntriesByType !== 'function') return false;
  const entry = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
  if (!entry || typeof entry.name !== 'string' || !entry.name) return false;
  try {
    return !isOwnerAgentConsolePath(new URL(entry.name).pathname);
  } catch {
    return false;
  }
}

/**
 * The strict CSP (proxy.ts) applies per document. App Router navigations are
 * client-side, so arriving from another admin page would keep that page's
 * broader policy — reload once to get the console's own document. Leaving the
 * console reloads too, so other admin pages get their normal policy back. The
 * unlock survives these reloads: it is an HttpOnly cookie, and the shell
 * re-reads GET /me on load, so the console comes back already unlocked.
 */
function useStrictConsoleDocument(): boolean {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    if (loadedOutsideConsole()) {
      window.location.reload();
      return;
    }
    setReady(true);
    return () => {
      window.setTimeout(() => {
        if (!isOwnerAgentConsolePath(window.location.pathname)) window.location.reload();
      }, 0);
    };
  }, []);
  return ready;
}

function ConsoleGate({ state, children }: { state: UseOwnerAgentReturn; children: ReactNode }) {
  if (!state.me) {
    if (state.meState === 'error') {
      return (
        <EmptyState
          variant="error"
          illustration={<ShieldAlert />}
          title="Agent console unavailable"
          description={state.meError ?? 'The owner console API could not be reached.'}
          primaryAction={{ label: 'Retry', onClick: () => void state.refreshMe() }}
        />
      );
    }
    return (
      <div className="flex min-h-[40vh] items-center justify-center text-sm text-admin-fg-muted" role="status">
        <Loader2 className="mr-2 h-4 w-4 animate-spin motion-reduce:animate-none" aria-hidden="true" /> Checking owner access…
      </div>
    );
  }
  if (!state.isOwner) {
    return (
      <EmptyState
        illustration={<ShieldAlert />}
        title="Owner only"
        description="The agent console is limited to the platform owner's account."
      />
    );
  }
  if (!state.featureEnabled) {
    return (
      <EmptyState
        illustration={<PowerOff />}
        title="Agent console is switched off"
        description="The owner_agent_console feature flag is off, so every console endpoint is disabled."
      />
    );
  }
  if (!state.unlocked) {
    return (
      <UnlockGate
        onUnlock={state.unlockConsole}
        clearedReason={state.unlock.clearedReason}
        blockedUntil={state.me?.unlockBlockedUntil ?? null}
      />
    );
  }
  return (
    <>
      {/* Same container as AdminPageShell so the tabs line up with the page header. */}
      <div className="mx-auto w-full max-w-[1440px] px-4 pt-4 sm:px-6 lg:px-8">
        <ConsoleNav />
      </div>
      {children}
    </>
  );
}

/**
 * Wraps every /admin/agent-console page: strict-document boundary, owner and
 * feature checks, the password + TOTP unlock gate (1-hour HttpOnly cookie; the
 * state is re-read from /me on every load), the Sessions / History / Settings
 * tabs, and the single `useOwnerAgent()` instance (so polling and the lease
 * heartbeat run once).
 */
export function OwnerAgentConsoleShell({ children }: { children: ReactNode }) {
  const ready = useStrictConsoleDocument();
  if (!ready) {
    return (
      <div className="flex min-h-[40vh] items-center justify-center text-sm text-admin-fg-muted" role="status">
        <Bot className="mr-2 h-4 w-4" aria-hidden="true" /> Opening the agent console…
      </div>
    );
  }
  return <OwnerAgentConsoleRuntime>{children}</OwnerAgentConsoleRuntime>;
}

function OwnerAgentConsoleRuntime({ children }: { children: ReactNode }) {
  const state = useOwnerAgent();
  return (
    <OwnerAgentConsoleContext.Provider value={state}>
      <ConsoleGate state={state}>{children}</ConsoleGate>
    </OwnerAgentConsoleContext.Provider>
  );
}
