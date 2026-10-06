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
import type { AbstractIntlMessages } from 'next-intl';
import { getRequestConfig } from 'next-intl/server';
import arCompanionMessages from './messages/ar/companion.json';
import arFreeSampleMessages from './messages/ar/free-samples.json';
import arWritingMessages from './messages/ar/writing.json';
import enCompanionMessages from './messages/en/companion.json';
import enFreeSampleMessages from './messages/en/free-samples.json';
import enWritingMessages from './messages/en/writing.json';

export const SUPPORTED_LOCALES = ['en', 'ar'] as const;
export type SupportedLocale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: SupportedLocale = 'en';

const LOCALE_COOKIE = 'lang';
const MESSAGE_MODULES = ['writing', 'companion', 'freeSamples'] as const;
type MessageModule = (typeof MESSAGE_MODULES)[number];

const MESSAGE_BUNDLES = {
  en: {
    writing: enWritingMessages,
    companion: enCompanionMessages,
    freeSamples: enFreeSampleMessages,
  },
  ar: {
    writing: arWritingMessages,
    companion: arCompanionMessages,
    freeSamples: arFreeSampleMessages,
  },
} satisfies Record<SupportedLocale, Record<MessageModule, Record<string, string>>>;

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

/**
 * Expand a flat map of dotted keys (`"writing.hub.cards.mocks.title"`) into the
 * nested object shape next-intl resolves against. next-intl 3.x treats every dot
 * in a `t('…')` lookup as a namespace separator, so a flat bundle never resolves
 * and silently falls back — authoring stays flat, but the runtime needs nesting.
 */
function unflattenMessages(flat: Record<string, string>): AbstractIntlMessages {
  const root: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(flat)) {
    const segments = key.split('.');
    let node = root;
    for (let i = 0; i < segments.length - 1; i += 1) {
      const segment = segments[i];
      const next = node[segment];
      if (typeof next !== 'object' || next === null) {
        node[segment] = {};
      }
      node = node[segment] as Record<string, unknown>;
    }
    node[segments[segments.length - 1]] = value;
  }
  return root as AbstractIntlMessages;
}

// Messages are static per locale, so each (locale, modules) set is merged and unflattened once per
// server process. The root layout and `getRequestConfig` both load them on every request; before
// this they redid the merge and the unflatten of ~700 keys each time. The cached objects are only
// ever read (they are serialised to the client or looked up), never mutated.
const messageCache = new Map<string, AbstractIntlMessages>();

function messagesFor(locale: SupportedLocale, modules: readonly MessageModule[]): AbstractIntlMessages {
  const cacheKey = `${locale}:${modules.join(',')}`;
  const cached = messageCache.get(cacheKey);
  if (cached) return cached;

  const baseline = MESSAGE_BUNDLES[DEFAULT_LOCALE];
  const localized = MESSAGE_BUNDLES[locale];

  const merged = modules.reduce<Record<string, string>>((messages, moduleName) => {
    return {
      ...messages,
      ...baseline[moduleName],
      ...(localized[moduleName] ?? {}),
    };
  }, {});

  const unflattened = unflattenMessages(merged);
  messageCache.set(cacheKey, unflattened);
  return unflattened;
}

const NON_WRITING_MODULES = MESSAGE_MODULES.filter((moduleName) => moduleName !== 'writing');

export interface LoadMessagesOptions {
  /**
   * Include the Writing bundle (default true). It is ~50 KB (en) / ~65 KB (ar) and the root
   * layout inlines whatever it is given into every HTML response, including sign-in, admin and
   * expert pages that never show Writing copy. The root layout therefore passes `false` and the
   * learner layout supplies the Writing bundle for the learner routes (see `loadWritingMessages`).
   */
  includeWriting?: boolean;
}

export async function loadAllMessages(
  locale: SupportedLocale,
  options: LoadMessagesOptions = {},
): Promise<AbstractIntlMessages> {
  return messagesFor(locale, options.includeWriting === false ? NON_WRITING_MODULES : MESSAGE_MODULES);
}

/** The Writing module's messages alone, for the routes that show Writing copy. */
export async function loadWritingMessages(locale: SupportedLocale): Promise<AbstractIntlMessages> {
  return messagesFor(locale, ['writing']);
}

export default getRequestConfig(async () => {
  const locale = await resolveLocale();
  const messages = await loadAllMessages(locale);
  return { locale, messages };
});
