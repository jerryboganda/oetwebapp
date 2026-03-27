'use client';

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import {
  clearStoredAuthSession,
  fetchCurrentAuthUser,
  getStoredAuthUser,
  getStoredAccessToken,
  isApiError,
  loginWithPassword,
  setStoredAuthSession,
  type AuthUser,
} from '@/lib/api';

const IS_DEV = process.env.NODE_ENV === 'development';
const ENABLE_MOCK_AUTH = process.env.NEXT_PUBLIC_ENABLE_MOCK_AUTH === 'true';

interface AuthState {
  user: AuthUser | null;
  loading: boolean;
  error: string | null;
}

interface AuthContextValue extends AuthState {
  signIn: (email: string, password: string) => Promise<void>;
  signUp: (_email: string, _password: string) => Promise<void>;
  signOut: () => Promise<void>;
  mockSignIn: () => void;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const MOCK_USER: AuthUser = {
  userId: 'mock-user-001',
  email: 'learner@oet-prep.dev',
  displayName: 'Faisal Maqsood',
  role: 'learner',
  isActive: true,
};

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(() => {
    const token = getStoredAccessToken();
    const cachedUser = getStoredAuthUser();

    if (IS_DEV && ENABLE_MOCK_AUTH && !token) {
      return { user: MOCK_USER, loading: false, error: null };
    }

    return {
      user: cachedUser,
      loading: !!token,
      error: null,
    };
  });

  const refreshUser = useCallback(async () => {
    const token = getStoredAccessToken();

    if (!token) {
      return;
    }

    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const user = await fetchCurrentAuthUser();
      setStoredAuthSession(token, user);
      setState({ user, loading: false, error: null });
    } catch (error) {
      clearStoredAuthSession();
      setState({ user: null, loading: false, error: isApiError(error) ? error.userMessage : 'Unable to restore your session.' });
    }
  }, []);

  useEffect(() => {
    if (!getStoredAccessToken()) {
      return;
    }

    const timer = window.setTimeout(() => {
      void refreshUser();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [refreshUser]);

  const signIn = useCallback(async (email: string, password: string) => {
    if (IS_DEV && ENABLE_MOCK_AUTH) {
      setState({ user: MOCK_USER, loading: false, error: null });
      return;
    }

    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const session = await loginWithPassword(email, password);
      setStoredAuthSession(session.accessToken, session.user);
      setState({ user: session.user, loading: false, error: null });
    } catch (error) {
      setState({ user: null, loading: false, error: isApiError(error) ? error.userMessage : 'Unable to sign in.' });
      throw error;
    }
  }, []);

  const signUp = useCallback(async () => {
    throw new Error('Self-service sign-up is not available in this build.');
  }, []);

  const signOut = useCallback(async () => {
    clearStoredAuthSession();
    setState({ user: null, loading: false, error: null });
  }, []);

  const mockSignIn = useCallback(() => {
    if (!IS_DEV || !ENABLE_MOCK_AUTH) {
      return;
    }

    setState({ user: MOCK_USER, loading: false, error: null });
  }, []);

  return (
    <AuthContext.Provider value={{ ...state, signIn, signUp, signOut, mockSignIn, refreshUser }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return context;
}
