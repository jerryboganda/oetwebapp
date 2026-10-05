/**
 * next-intl error handling shared by every `NextIntlClientProvider` in the app. A nested provider
 * (see components/providers/extra-messages-provider.tsx) does not inherit these from its parent, so
 * they live here and each provider is given the same ones.
 */

export function isWritingMessageKey(key: string) {
  return key === 'writing' || key.startsWith('writing.');
}

export function getMessageFallback({ key }: { key: string }) {
  if (!isWritingMessageKey(key)) return key;

  console.error(new Error(`Missing required writing translation: ${key}`));
  return 'Writing copy unavailable';
}

/**
 * Pages that don't have a translation for a requested key (or that don't use next-intl at all) keep
 * their existing English strings — missing keys must not throw a runtime error inside legacy pages
 * while the rollout is partial. Writing keys still surface through `getMessageFallback`.
 */
export function ignoreIntlError() {
  /* Non-writing pages still use key fallbacks during the partial rollout. */
}
