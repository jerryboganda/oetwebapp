import { useState } from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import type { UserRole } from '@/lib/types/auth';
import { renderWithRouter } from '@/tests/test-utils';
import type { NavGroup } from '../sidebar';

const mocks = vi.hoisted(() => ({
  searchContent: vi.fn(),
  isVisible: (() => true) as (item: { href: string }) => boolean,
  visibilityActive: [] as boolean[],
}));

vi.mock('@/lib/api', () => ({ searchContent: mocks.searchContent }));

// The gating rules themselves are covered by use-learner-nav-visibility.test.tsx;
// here only that the palette applies them, and only for learners.
vi.mock('@/hooks/use-learner-nav-visibility', () => ({
  useLearnerNavVisibility: (_items: unknown, active: boolean) => {
    mocks.visibilityActive.push(active);
    return mocks.isVisible;
  },
}));

import { GlobalSearch, LEARNER_ACCOUNT_DESTINATIONS, SearchTrigger } from '../global-search';

const LEARNER_SECTIONS: NavGroup[] = [
  {
    label: 'Practice',
    items: [
      { href: '/', label: 'Dashboard', icon: <span /> },
      { href: '/listening', label: 'Listening', sidebarLabel: 'Listening Practice', icon: <span /> },
      { href: '/mocks', label: 'Mocks', moduleKey: 'Mocks', icon: <span /> },
    ],
  },
];

const ADMIN_SECTIONS: NavGroup[] = [
  { label: 'People', items: [{ href: '/admin/users', label: 'Users', icon: <span /> }] },
  { label: 'System', items: [{ href: '/admin/settings', label: 'Runtime Settings', icon: <span /> }] },
];

function Harness({ sections, role, otherDialog }: { sections: NavGroup[]; role: UserRole; otherDialog: boolean }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <SearchTrigger onClick={() => setOpen(true)} />
      {otherDialog ? <div role="dialog" aria-label="Other dialog" /> : null}
      <GlobalSearch open={open} onOpenChange={setOpen} sections={sections} workspaceRole={role} />
    </>
  );
}

function renderPalette({ sections = LEARNER_SECTIONS, role = 'learner', otherDialog = false }: { sections?: NavGroup[]; role?: UserRole; otherDialog?: boolean } = {}) {
  const push = vi.fn();
  renderWithRouter(<Harness sections={sections} role={role} otherDialog={otherDialog} />, { router: { push } });
  return push;
}

const pressHotkey = (modifier: { ctrlKey?: boolean; metaKey?: boolean } = { ctrlKey: true }) =>
  fireEvent.keyDown(document.body, { key: 'k', ...modifier });
const palette = () => screen.queryByRole('dialog', { name: 'Search' });
const optionHrefs = () => screen.queryAllByRole('option').map((option) => option.getAttribute('data-href'));
const flush = async (ms: number) => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
};

describe('GlobalSearch command palette', () => {
  beforeEach(() => {
    mocks.searchContent.mockReset();
    mocks.searchContent.mockResolvedValue({ items: [] });
    mocks.isVisible = () => true;
    mocks.visibilityActive.length = 0;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('toggles with Ctrl+K and ⌘K and focuses the combobox', async () => {
    renderPalette();

    pressHotkey();
    expect(palette()).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Search' })).toHaveFocus());

    pressHotkey();
    expect(palette()).not.toBeInTheDocument();

    pressHotkey({ metaKey: true });
    expect(palette()).toBeInTheDocument();
  });

  it('closes on Escape', () => {
    renderPalette();
    pressHotkey();

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(palette()).not.toBeInTheDocument();
  });

  it('does not open over another dialog, including a Radix one without aria-modal', () => {
    renderPalette({ otherDialog: true });

    pressHotkey();
    expect(palette()).not.toBeInTheDocument();
  });

  it('opens from the trigger, which advertises the platform shortcut', () => {
    renderPalette();
    const trigger = screen.getByRole('button', { name: 'Search anything' });

    expect(trigger).toHaveAttribute('aria-keyshortcuts', 'Control+K Meta+K');
    expect(trigger).toHaveTextContent('Ctrl K');
    fireEvent.click(trigger);
    expect(palette()).toBeInTheDocument();
  });

  it('lists only the passed nav plus the learner account pages', () => {
    renderPalette();
    pressHotkey();

    expect(optionHrefs()).toEqual(['/', '/listening', '/mocks', ...LEARNER_ACCOUNT_DESTINATIONS.map((destination) => destination.href)]);
    expect(screen.getByRole('group', { name: 'Go to' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Listening Practice' })).toBeInTheDocument();
  });

  it('matches the nav label and the sidebar label, case-insensitively', () => {
    renderPalette();
    pressHotkey();
    const combobox = screen.getByRole('combobox', { name: 'Search' });

    fireEvent.change(combobox, { target: { value: 'PRACTICE' } });
    expect(optionHrefs()).toEqual(['/listening']);

    fireEvent.change(combobox, { target: { value: 'moc' } });
    expect(optionHrefs()).toEqual(['/mocks']);
  });

  it('applies the learner nav gating', () => {
    mocks.isVisible = (item) => item.href !== '/mocks';
    renderPalette();
    pressHotkey();

    expect(mocks.visibilityActive).toContain(true);
    expect(optionHrefs()).not.toContain('/mocks');
  });

  it('moves the active option with the arrow keys and navigates on Enter', () => {
    const push = renderPalette();
    pressHotkey();
    const combobox = screen.getByRole('combobox', { name: 'Search' });
    const options = screen.getAllByRole('option');
    const last = options[options.length - 1];

    expect(combobox).toHaveAttribute('aria-activedescendant', options[0].id);
    fireEvent.keyDown(combobox, { key: 'ArrowDown' });
    expect(combobox).toHaveAttribute('aria-activedescendant', options[1].id);
    expect(options[1]).toHaveAttribute('aria-selected', 'true');

    fireEvent.keyDown(combobox, { key: 'ArrowUp' });
    fireEvent.keyDown(combobox, { key: 'ArrowUp' });
    expect(combobox).toHaveAttribute('aria-activedescendant', last.id);

    const lastHref = last.getAttribute('data-href');
    fireEvent.keyDown(combobox, { key: 'Enter' });
    expect(push).toHaveBeenCalledWith(lastHref);
    expect(palette()).not.toBeInTheDocument();
  });

  it('debounces content search into one request and links hits to their module', async () => {
    vi.useFakeTimers();
    mocks.searchContent.mockResolvedValue({ items: [{ id: 'c1', title: 'Cardiology referral letter', subtestCode: 'Writing' }] });
    renderPalette();
    pressHotkey();
    const combobox = screen.getByRole('combobox', { name: 'Search' });

    for (const value of ['ca', 'car', 'card']) fireEvent.change(combobox, { target: { value } });
    await flush(299);
    expect(mocks.searchContent).not.toHaveBeenCalled();

    await flush(1);
    expect(mocks.searchContent).toHaveBeenCalledTimes(1);
    expect(mocks.searchContent).toHaveBeenCalledWith({ q: 'card', pageSize: 8 });
    expect(screen.getByRole('group', { name: 'Content' })).toBeInTheDocument();
    expect(optionHrefs()).toEqual(['/writing']);
  });

  it('falls back to pages only when content search fails', async () => {
    vi.useFakeTimers();
    mocks.searchContent.mockRejectedValue(new Error('search unavailable'));
    renderPalette();
    pressHotkey();

    fireEvent.change(screen.getByRole('combobox', { name: 'Search' }), { target: { value: 'listening' } });
    await flush(300);

    expect(screen.getByText(/showing pages only/i)).toBeInTheDocument();
    expect(optionHrefs()).toEqual(['/listening']);
  });

  it('never calls content search or learner gating for staff, and labels each nav section', async () => {
    vi.useFakeTimers();
    renderPalette({ sections: ADMIN_SECTIONS, role: 'admin' });
    pressHotkey();

    expect(screen.getByRole('group', { name: 'People' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'System' })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Account' })).not.toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: 'Search' }), { target: { value: 'users' } });
    await flush(1000);

    expect(mocks.searchContent).not.toHaveBeenCalled();
    expect(mocks.visibilityActive).not.toContain(true);
    expect(optionHrefs()).toEqual(['/admin/users']);
  });

  it('uses design-token text sizes only', () => {
    renderPalette();
    pressHotkey();

    expect(document.body.innerHTML).not.toMatch(/text-\[\d/);
  });
});
