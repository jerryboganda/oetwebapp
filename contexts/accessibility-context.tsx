'use client';

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { MotionConfig } from 'motion/react';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '@/contexts/auth-context';
import { fetchSettingsSection } from '@/lib/api';
import { queryKeys } from '@/lib/query/hooks';

/**
 * Learner-facing accessibility preferences (Settings → Accessibility).
 *
 * These are persisted server-side via `PATCH /v1/settings/accessibility` and,
 * until this provider existed, were saved but never read — toggling them changed
 * nothing on screen. This provider reads the same react-query cache the settings
 * page writes to, so saving on the settings page applies the change live, and
 * every subsequent page load re-applies it before first paint (from the
 * localStorage mirror) to avoid a flash.
 */
export interface AccessibilityPreferences {
  largeText: boolean;
  highContrast: boolean;
  reduceMotion: boolean;
  keyboardHints: boolean;
}

const DEFAULT_PREFERENCES: AccessibilityPreferences = {
  largeText: false,
  highContrast: false,
  reduceMotion: false,
  keyboardHints: false,
};

// Mirrors the last-known preferences so they apply on the very next load,
// before the authenticated settings fetch resolves.
const STORAGE_KEY = 'oet.accessibility-preferences';

const AccessibilityContext = createContext<AccessibilityPreferences>(DEFAULT_PREFERENCES);

export function useAccessibilityPreferences(): AccessibilityPreferences {
  return useContext(AccessibilityContext);
}

function coerce(values: Record<string, unknown> | null | undefined): AccessibilityPreferences {
  const source = values ?? {};
  return {
    largeText: Boolean(source.largeText),
    highContrast: Boolean(source.highContrast),
    reduceMotion: Boolean(source.reduceMotion),
    keyboardHints: Boolean(source.keyboardHints),
  };
}

function readCachedPreferences(): AccessibilityPreferences {
  if (typeof window === 'undefined') return DEFAULT_PREFERENCES;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw ? coerce(JSON.parse(raw) as Record<string, unknown>) : DEFAULT_PREFERENCES;
  } catch {
    return DEFAULT_PREFERENCES;
  }
}

function applyToDocument(prefs: AccessibilityPreferences) {
  if (typeof document === 'undefined') return;
  const root = document.documentElement;
  root.classList.toggle('a11y-large-text', prefs.largeText);
  root.classList.toggle('a11y-high-contrast', prefs.highContrast);
  root.classList.toggle('a11y-reduce-motion', prefs.reduceMotion);
  if (prefs.keyboardHints) {
    root.setAttribute('data-keyboard-hints', 'true');
  } else {
    root.removeAttribute('data-keyboard-hints');
  }
}

export function AccessibilityProvider({ children }: { children: ReactNode }) {
  const { user, isAuthenticated, loading } = useAuth();
  const queryUserId = user?.userId ?? 'current';

  // Seed from the localStorage mirror so a returning learner's settings apply
  // immediately; the server fetch below reconciles it.
  const [cached] = useState<AccessibilityPreferences>(readCachedPreferences);

  // `/v1/settings/*` is learner-only server-side (experts have no Users row and
  // get a 403), so only learners fetch the section — other roles keep the
  // localStorage mirror without generating 403 noise on every page.
  const sectionQuery = useQuery({
    queryKey: queryKeys.settings.section(queryUserId, 'accessibility'),
    queryFn: () => fetchSettingsSection('accessibility'),
    enabled: isAuthenticated && !loading && user?.role === 'learner',
    staleTime: 60_000,
    retry: false,
  });

  const preferences = useMemo<AccessibilityPreferences>(() => {
    if (sectionQuery.data) return coerce(sectionQuery.data.values as Record<string, unknown>);
    return cached;
  }, [sectionQuery.data, cached]);

  useEffect(() => {
    applyToDocument(preferences);
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
    } catch {
      // Private-mode / storage-disabled: applying to the DOM already succeeded.
    }
  }, [preferences]);

  // Clear the DOM flags on unmount (e.g. hot reload) so stale classes don't linger.
  useEffect(() => () => applyToDocument(DEFAULT_PREFERENCES), []);

  return (
    <AccessibilityContext.Provider value={preferences}>
      {/*
        `reducedMotion="always"` makes every motion/react component that calls
        useReducedMotion() (all of components/ui/motion-primitives) collapse its
        animation — no per-component wiring needed. `"user"` falls back to the
        OS prefers-reduced-motion media query when the learner hasn't opted in.
      */}
      <MotionConfig reducedMotion={preferences.reduceMotion ? 'always' : 'user'}>
        {children}
      </MotionConfig>
    </AccessibilityContext.Provider>
  );
}
