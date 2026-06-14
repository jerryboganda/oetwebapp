/**
 * next-intl request config. Loaded by `getRequestConfig` on every server
 * request. Locale is sourced from the `lang` cookie (preferred), then the
 * `Accept-Language` header, then falls back to `en`.
 *
 * Message bundles live under `messages/{locale}/<module>.json`. Today only
 * the Writing module is internationalised — the rest of the app still renders
 * English inline. The loader gracefully degrades when a module bundle is
 * missing for the requested locale (falls back to English) so adding more
 * locales later doesn't require backfilling every module bundle at once.
 */
import { cookies, headers } from 'next/headers';
import { getRequestConfig } from 'next-intl/server';

export const SUPPORTED_LOCALES = ['en', 'ar'] as const;
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: SupportedLocale = 'en';

/** Modules with translation bundles under `messages/{locale}/<module>.json`. */
const MESSAGE_MODULES = ['writing'] as const;

const LOCALE_COOKIE = 'lang';

function isSupportedLocale(value: string | null | undefined): value is SupportedLocale {
  return !!value && (SUPPORTED_LOCALES as readonly string[]).includes(value);
}

/**
 * Parse a single primary tag (e.g. `ar`, `ar-SA`, `en-GB`) out of an
 * Accept-Language header and reduce it to one of our supported locales.
 */
function pickFromAcceptLanguage(header: string | null): SupportedLocale | null {
  if (!header) return null;
  const tags = header
    .split(',')
    .map((entry) => entry.split(';')[0]?.trim().toLowerCase())
    .filter(Boolean) as string[];
  for (const tag of tags) {
    const primary = tag.split('-')[0];
    if (isSupportedLocale(primary)) return primary;
  }
  return null;
}

export async function resolveLocale(): Promise<SupportedLocale> {
  try {
    const cookieStore = await cookies();
    const fromCookie = cookieStore.get(LOCALE_COOKIE)?.value;
    if (isSupportedLocale(fromCookie)) return fromCookie;
    const headerStore = await headers();
    const fromHeader = pickFromAcceptLanguage(headerStore.get('accept-language'));
    if (fromHeader) return fromHeader;
  } catch {
    /* `cookies()`/`headers()` throw outside a request scope — fall through */
  }
  return DEFAULT_LOCALE;
}

async function importMessagesForModule(
  locale: SupportedLocale,
  moduleName: string,
): Promise<Record<string, string>> {
  try {
    const mod = (await import(`./messages/${locale}/${moduleName}.json`)) as { default: Record<string, string> };
    return mod.default;
  } catch {
    return {};
  }
}

async function loadMessagesForModule(
  locale: SupportedLocale,
  moduleName: string,
): Promise<Record<string, string>> {
  const baseline = await importMessagesForModule(DEFAULT_LOCALE, moduleName);
  if (locale === DEFAULT_LOCALE) return baseline;

  const localized = await importMessagesForModule(locale, moduleName);
  return { ...baseline, ...localized };
}

export async function loadAllMessages(locale: SupportedLocale): Promise<Record<string, string>> {
  const buckets = await Promise.all(MESSAGE_MODULES.map((mod) => loadMessagesForModule(locale, mod)));
  return Object.assign({}, ...buckets) as Record<string, string>;
}

export default getRequestConfig(async () => {
  const locale = await resolveLocale();
  const messages = await loadAllMessages(locale);
  return { locale, messages };
});
