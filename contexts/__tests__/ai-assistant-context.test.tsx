import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';

const mocks = vi.hoisted(() => ({
  auth: {
    session: null as { accessToken: string } | null,
    user: null as { userId: string; role: string } | null,
  },
  useAiAssistant: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mocks.auth,
}));

vi.mock('@/hooks/use-ai-assistant', () => ({
  useAiAssistant: (...args: unknown[]) => mocks.useAiAssistant(...args),
}));

import { AiAssistantProvider, useAiAssistantContext } from '../ai-assistant-context';

const assistantStub = { connectionState: 'disconnected', isConnected: false };

function wrapper({ children }: { children: ReactNode }) {
  return <AiAssistantProvider>{children}</AiAssistantProvider>;
}

function lastHookOptions() {
  return mocks.useAiAssistant.mock.lastCall?.[0] as { token: string | null; autoConnect: boolean };
}

describe('AiAssistantProvider lazy connect', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.useAiAssistant.mockReturnValue(assistantStub);
    mocks.auth.session = { accessToken: 'access-1' };
    mocks.auth.user = { userId: 'learner-1', role: 'learner' };
  });

  it('does not ask the hook to connect until a surface activates the assistant', () => {
    renderHook(() => useAiAssistantContext(), { wrapper });

    expect(lastHookOptions()).toEqual({ token: 'access-1', autoConnect: false });
  });

  it('connects after activate() and keeps the connection across re-renders', () => {
    const { result, rerender } = renderHook(() => useAiAssistantContext(), { wrapper });

    act(() => result.current.activate());
    expect(lastHookOptions()).toEqual({ token: 'access-1', autoConnect: true });

    rerender();
    expect(lastHookOptions().autoConnect).toBe(true);
  });

  it('activates when the panel is opened or toggled', () => {
    const { result } = renderHook(() => useAiAssistantContext(), { wrapper });

    act(() => result.current.open());
    expect(result.current.isOpen).toBe(true);
    expect(lastHookOptions().autoConnect).toBe(true);
  });

  it('never hands a token to the hook for roles without assistant access', () => {
    mocks.auth.user = { userId: 'sponsor-1', role: 'sponsor' };
    const { result } = renderHook(() => useAiAssistantContext(), { wrapper });

    act(() => result.current.activate());

    expect(result.current.hasAccess).toBe(false);
    expect(lastHookOptions().token).toBeNull();
  });

  it('does not carry the activation over to a different account', () => {
    const { result, rerender } = renderHook(() => useAiAssistantContext(), { wrapper });
    act(() => result.current.activate());
    expect(lastHookOptions().autoConnect).toBe(true);

    mocks.auth.user = { userId: 'learner-2', role: 'learner' };
    rerender();

    expect(lastHookOptions().autoConnect).toBe(false);
  });

  it('clears the activation on sign-out so the next sign-in starts lazy again', () => {
    const { result, rerender } = renderHook(() => useAiAssistantContext(), { wrapper });
    act(() => result.current.activate());

    mocks.auth.session = null;
    mocks.auth.user = null;
    rerender();

    mocks.auth.session = { accessToken: 'access-2' };
    mocks.auth.user = { userId: 'learner-1', role: 'learner' };
    rerender();

    expect(lastHookOptions()).toEqual({ token: 'access-2', autoConnect: false });
  });
});
