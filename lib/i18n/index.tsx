'use client';

import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import en, { type LocaleStrings } from './en';

// ── Supported Locales ──────────────────────────────────────────
export type SupportedLocale = 'en' | 'ar';

export const LOCALE_META: Record<SupportedLocale, { label: string; dir: 'ltr' | 'rtl'; nativeName: string }> = {
  en: { label: 'English', dir: 'ltr', nativeName: 'English' },
  ar: { label: 'Arabic', dir: 'rtl', nativeName: 'العربية' },
};

const LOCALE_STORAGE_KEY = 'oet.locale';

// ── Lazy-loaded locale bundles ────────────────────────────────
const localeLoaders: Record<SupportedLocale, () => Promise<{ default: LocaleStrings }>> = {
  en: () => Promise.resolve({ default: en }),
  ar: () => import('./ar'),
};

// ── Context ──────────────────────────────────────────────────
interface LocaleContextValue {
  locale: SupportedLocale;
  dir: 'ltr' | 'rtl';
  t: LocaleStrings;
  setLocale: (locale: SupportedLocale) => void;
  availableLocales: typeof LOCALE_META;
}

const LocaleContext = createContext<LocaleContextValue>({
  locale: 'en',
  dir: 'ltr',
  t: en,
  setLocale: () => {},
  availableLocales: LOCALE_META,
});

// ── Provider ─────────────────────────────────────────────────
interface LocaleProviderProps {
  children: ReactNode;
  defaultLocale?: SupportedLocale;
}

export function LocaleProvider({ children, defaultLocale = 'en' }: LocaleProviderProps) {
  const [locale, setLocaleState] = useState<SupportedLocale>(() => {
    if (typeof window === 'undefined') return defaultLocale;
    const stored = window.localStorage.getItem(LOCALE_STORAGE_KEY) as SupportedLocale | null;
    return stored && LOCALE_META[stored] ? stored : defaultLocale;
  });
  const [strings, setStrings] = useState<LocaleStrings>(en);
  const dir = LOCALE_META[locale].dir;

  // Load locale strings when locale changes
  useEffect(() => {
    let cancelled = false;
    localeLoaders[locale]().then((mod) => {
      if (!cancelled) setStrings(mod.default);
    });
    return () => { cancelled = true; };
  }, [locale]);

  // Update document direction and language
  useEffect(() => {
    if (typeof document === 'undefined') return;
    document.documentElement.dir = dir;
    document.documentElement.lang = locale;
  }, [locale, dir]);

  const setLocale = useCallback((newLocale: SupportedLocale) => {
    if (!LOCALE_META[newLocale]) return;
    setLocaleState(newLocale);
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, newLocale);
    }
  }, []);

  return (
    <LocaleContext.Provider value={{ locale, dir, t: strings, setLocale, availableLocales: LOCALE_META }}>
      {children}
    </LocaleContext.Provider>
  );
}

// ── Hook ──────────────────────────────────────────────────────
export function useLocale() {
  return useContext(LocaleContext);
}

// ── Utility: get nested key from strings ─────────────────────
export function getNestedString(strings: LocaleStrings, path: string): string {
  const keys = path.split('.');
  let current: unknown = strings;
  for (const key of keys) {
    if (current && typeof current === 'object' && key in current) {
      current = (current as Record<string, unknown>)[key];
    } else {
      return path; // Fallback to key path
    }
  }
  return typeof current === 'string' ? current : path;
}