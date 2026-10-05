'use client';

import { useMemo, type ReactNode } from 'react';
import { NextIntlClientProvider, useLocale, useMessages, type AbstractIntlMessages } from 'next-intl';
import { getMessageFallback, ignoreIntlError } from '@/lib/i18n/message-fallback';

type MessageTree = Record<string, unknown>;

function isMessageTree(value: unknown): value is MessageTree {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Deep merge of two message trees; the second wins on a leaf. Neither input is mutated. */
export function mergeMessageTrees(base: MessageTree, extra: MessageTree): MessageTree {
  const merged: MessageTree = { ...base };
  for (const [key, value] of Object.entries(extra)) {
    const existing = merged[key];
    merged[key] = isMessageTree(existing) && isMessageTree(value) ? mergeMessageTrees(existing, value) : value;
  }
  return merged;
}

/**
 * Adds a message bundle to the messages the root provider already supplies, for one part of the
 * tree. The root layout no longer inlines the Writing bundle into every HTML response; the learner
 * layout hands it to this provider instead, so only learner routes carry it.
 *
 * A nested next-intl provider replaces rather than extends its parent, so the parent's messages are
 * read back and merged in, and the same error handling is given to it (it is not inherited).
 */
export function ExtraMessagesProvider({
  messages,
  children,
}: {
  messages: AbstractIntlMessages;
  children: ReactNode;
}) {
  const locale = useLocale();
  const parentMessages = useMessages();
  const merged = useMemo(
    () => mergeMessageTrees(parentMessages as MessageTree, messages as MessageTree) as AbstractIntlMessages,
    [parentMessages, messages],
  );

  return (
    <NextIntlClientProvider
      locale={locale}
      messages={merged}
      onError={ignoreIntlError}
      getMessageFallback={getMessageFallback}
    >
      {children}
    </NextIntlClientProvider>
  );
}
