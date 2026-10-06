import { render, screen } from '@testing-library/react';
import { NextIntlClientProvider, useTranslations } from 'next-intl';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ExtraMessagesProvider, mergeMessageTrees } from '../extra-messages-provider';

// The global test setup replaces next-intl with a key-echoing stub; this suite is about the real
// provider semantics (a nested provider replaces its parent's messages), so restore the module.
vi.mock('next-intl', async (importOriginal) => importOriginal());

function Probe({ messageKey }: { messageKey: string }) {
  const t = useTranslations();
  return <p data-testid="out">{t(messageKey)}</p>;
}

function renderWithRoot(messageKey: string, extra: Record<string, unknown>, root: Record<string, unknown>) {
  return render(
    <NextIntlClientProvider locale="en" messages={root as never} onError={() => undefined}>
      <ExtraMessagesProvider messages={extra as never}>
        <Probe messageKey={messageKey} />
      </ExtraMessagesProvider>
    </NextIntlClientProvider>,
  );
}

const root = { companion: { title: 'Talk to Jana' }, freeSample: { completed: 'Done' } };
const writing = { writing: { hub: { pageTitle: 'Writing hub' } } };

describe('ExtraMessagesProvider', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('adds the extra bundle and keeps everything the root provider supplied', () => {
    renderWithRoot('writing.hub.pageTitle', writing, root);
    expect(screen.getByTestId('out')).toHaveTextContent('Writing hub');
  });

  it('still resolves the root provider messages below it', () => {
    renderWithRoot('companion.title', writing, root);
    expect(screen.getByTestId('out')).toHaveTextContent('Talk to Jana');
  });

  it('keeps the Writing fallback for a missing Writing key: it is logged and never shows a raw key', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    renderWithRoot('writing.hub.doesNotExist', writing, root);

    expect(screen.getByTestId('out')).toHaveTextContent('Writing copy unavailable');
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ message: 'Missing required writing translation: writing.hub.doesNotExist' }));
  });

  it('lets a missing non-Writing key fall back to the key, as the root provider does', () => {
    renderWithRoot('companion.nothing', writing, root);
    expect(screen.getByTestId('out')).toHaveTextContent('companion.nothing');
  });
});

describe('mergeMessageTrees', () => {
  it('merges nested trees and lets the second win on a leaf, without mutating either', () => {
    const base = { a: { x: '1', y: '2' }, b: 'base' };
    const extra = { a: { y: 'override', z: '3' }, c: 'new' };

    expect(mergeMessageTrees(base, extra)).toEqual({ a: { x: '1', y: 'override', z: '3' }, b: 'base', c: 'new' });
    expect(base).toEqual({ a: { x: '1', y: '2' }, b: 'base' });
    expect(extra).toEqual({ a: { y: 'override', z: '3' }, c: 'new' });
  });
});
