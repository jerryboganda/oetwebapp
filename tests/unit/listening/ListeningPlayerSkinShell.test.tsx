import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ListeningPlayerSkinShell } from '@/components/domain/listening/player/skins/ListeningPlayerSkinShell';
import { presentationModeFromSession } from '@/lib/listening/modes';

afterEach(() => {
  cleanup();
});

describe('presentationModeFromSession', () => {
  it.each([
    [{ presentationStyle: 'kiosk_fullscreen' as const }, 'home'],
    [{ presentationStyle: 'printable_booklet' as const }, 'paper'],
    [{ presentationStyle: 'exam_standard' as const }, 'computer'],
    [{ presentationStyle: 'practice' as const }, 'computer'],
    [{ mode: 'home' as const }, 'home'],
    [{ mode: 'paper' as const }, 'paper'],
    [{ mode: 'exam' as const }, 'computer'],
    [{ mode: 'practice' as const }, 'computer'],
    [{}, 'computer'],
  ])('maps %j to %s', (input, expected) => {
    expect(presentationModeFromSession(input)).toBe(expected);
  });

  it('prefers presentationStyle over mode when both are present', () => {
    expect(
      presentationModeFromSession({ mode: 'exam', presentationStyle: 'kiosk_fullscreen' }),
    ).toBe('home');
  });
});

describe('ListeningPlayerSkinShell', () => {
  it('renders computer skin as a pass-through with data-listening-skin', () => {
    render(
      <ListeningPlayerSkinShell mode="computer" enableSideEffects={false}>
        <p>Player goes here</p>
      </ListeningPlayerSkinShell>,
    );
    expect(screen.getByText('Player goes here')).toBeInTheDocument();
    expect(document.querySelector('[data-listening-skin="computer"]')).not.toBeNull();
  });

  it('renders home skin with kiosk banner and shield warning', () => {
    render(
      <ListeningPlayerSkinShell mode="home" enableSideEffects={false}>
        <p>Player goes here</p>
      </ListeningPlayerSkinShell>,
    );
    expect(screen.getByText(/OET@Home/i)).toBeInTheDocument();
    expect(screen.getByText(/kiosk mode/i)).toBeInTheDocument();
    expect(document.querySelector('[data-listening-skin="home"]')).not.toBeNull();
  });

  it('renders paper skin with print affordance', async () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const user = userEvent.setup();

    render(
      <ListeningPlayerSkinShell mode="paper" enableSideEffects={false}>
        <p>Player goes here</p>
      </ListeningPlayerSkinShell>,
    );
    expect(screen.getByText(/OET on Paper simulation/i)).toBeInTheDocument();
    const printButton = screen.getByRole('button', { name: /print booklet/i });
    await user.click(printButton);
    expect(printSpy).toHaveBeenCalledOnce();
    printSpy.mockRestore();
  });

  it('home skin blocks paste events when side effects are enabled', () => {
    render(
      <ListeningPlayerSkinShell mode="home" enableSideEffects>
        <input aria-label="any" />
      </ListeningPlayerSkinShell>,
    );
    const event = new Event('paste', { bubbles: true, cancelable: true });
    document.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it('home skin does NOT attach listeners when side effects are disabled', () => {
    render(
      <ListeningPlayerSkinShell mode="home" enableSideEffects={false}>
        <input aria-label="any" />
      </ListeningPlayerSkinShell>,
    );
    const event = new Event('paste', { bubbles: true, cancelable: true });
    document.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });
});
