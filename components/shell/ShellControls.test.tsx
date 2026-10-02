import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ShellControls } from './ShellControls';

vi.mock('./UpdateDialog', () => ({ UpdateDialog: () => null }));

// Launch handoff UI-2 (2 Oct 2026): the floating quick-access handle covered
// page content on phones, so it shows from lg up only; below lg its actions
// live in the TopNav mobile menu.
describe('ShellControls', () => {
  afterEach(() => {
    delete document.documentElement.dataset.runtimeKind;
  });

  it('renders nothing on the web', () => {
    document.documentElement.dataset.runtimeKind = 'web';
    const { container } = render(<ShellControls />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows the native-shell handle at lg+ only', () => {
    document.documentElement.dataset.runtimeKind = 'capacitor-native';
    render(<ShellControls />);

    const handle = screen.getByTestId('shell-controls-handle');
    expect(handle).toHaveAccessibleName('Open quick access menu');
    expect(handle.parentElement).toHaveClass('hidden', 'lg:block', 'fixed');
  });
});
