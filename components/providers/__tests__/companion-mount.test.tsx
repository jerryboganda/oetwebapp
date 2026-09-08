'use client';

import { render, screen } from '@testing-library/react';

const mountState = vi.hoisted(() => ({
  auth: { isAuthenticated: true, role: 'admin' as string | null },
  flags: {} as Record<string, boolean>,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mountState.auth,
}));

vi.mock('@/hooks/use-feature-flag-map', () => ({
  useFeatureFlagMap: () => mountState.flags,
}));

vi.mock('@/components/domain/ai-assistant', () => ({
  AiAssistantWidget: ({ role }: { role: unknown }) => (
    <div data-testid="assistant-widget" data-role={String(role)} />
  ),
}));

import { CompanionMount } from '../companion-mount';

describe('CompanionMount admin chatbot', () => {
  beforeEach(() => {
    mountState.auth.isAuthenticated = true;
    mountState.auth.role = 'admin';
    mountState.flags = {};
  });

  it('renders the bottom-right chatbot for admins without the learner flag', () => {
    render(<CompanionMount />);
    const widget = screen.getByTestId('assistant-widget');
    expect(widget.getAttribute('data-role')).toBe('admin');
  });

  it('keeps the learner companion behind its flag', () => {
    mountState.auth.role = 'learner';
    mountState.flags = {};
    const { unmount } = render(<CompanionMount />);
    expect(screen.queryByTestId('assistant-widget')).toBeNull();
    unmount();

    mountState.flags = { ai_learning_companion: true };
    render(<CompanionMount />);
    expect(screen.getByTestId('assistant-widget').getAttribute('data-role')).toBe('learner');
  });

  it('renders nothing for experts and visitors', () => {
    mountState.auth.role = 'expert';
    const { unmount } = render(<CompanionMount />);
    expect(screen.queryByTestId('assistant-widget')).toBeNull();
    unmount();

    mountState.auth.isAuthenticated = false;
    mountState.auth.role = null;
    render(<CompanionMount />);
    expect(screen.queryByTestId('assistant-widget')).toBeNull();
  });
});
