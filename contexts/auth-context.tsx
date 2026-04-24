'use client';

import {
  beginAuthenticatorSetup as beginAuthenticatorSetupRequest,
  completeMfaChallenge as completeMfaChallengeRequest,
  completeRecoveryChallenge as completeRecoveryChallengeRequest,
  confirmAuthenticatorSetup as confirmAuthenticatorSetupRequest,
  getPendingMfaChallenge,
  registerLearner as registerLearnerRequest,
  restoreSession,
  sendEmailVerificationOtp as sendEmailVerificationOtpRequest,
  signIn as signInWithBackend,
  signOut as signOutFromBackend,
  verifyEmailOtp as verifyEmailOtpRequest,
} from '@/lib/auth-client';
import type {
  AuthSession,
  AuthenticatorSetup,
  CurrentUser,
  OtpChallenge,
  PendingMfaChallenge,
  RegisterLearnerInput,
  SignInResult,
  UserRole,
} from '@/lib/types/auth';
import { initializeAnalyticsTransport } from '@/lib/analytics';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

interface AuthState {
  session: AuthSession | null;
  user: CurrentUser | null;
  loading: boolean;
  error: string | null;
  pendingMfaChallenge: PendingMfaChallenge | null;
}

interface AuthContextValue extends AuthState {
  role: UserRole | null;
  isAuthenticated: boolean;
  signIn: (email: string, password: string, rememberMe?: boolean) => Promise<SignInResult>;
  signUp: (input: RegisterLearnerInput) => Promise<AuthSession>;
  signOut: () => Promise<void>;
  refreshSession: () => Promise<AuthSession | null>;
  sendVerificationOtp: () => Promise<OtpChallenge>;
  verifyEmailOtp: (code: string) => Promise<CurrentUser>;
  beginAuthenticatorSetup: () => Promise<AuthenticatorSetup>;
  confirmAuthenticatorSetup: (code: string) => Promise<CurrentUser>;
  completeMfaChallenge: (code: string) => Promise<AuthSession>;
  completeRecoveryChallenge: (recoveryCode: string) => Promise<AuthSession>;
  clearError: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

function toMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Authentication request failed.';
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    session: null,
    user: null,
    loading: true,
    error: null,
    pendingMfaChallenge: null,
  });

  useEffect(() => {
    let cancelled = false;

    const hydrate = async () => {
      try {
        const session = await restoreSession();
        if (cancelled) {
          return;
        }

        setState({
          session,
          user: session?.currentUser ?? null,
          loading: false,
          error: null,
          pendingMfaChallenge: getPendingMfaChallenge(),
        });
      } catch (error) {
        if (cancelled) {
          return;
        }

        setState({
          session: null,
          user: null,
          loading: false,
          error: toMessage(error),
          pendingMfaChallenge: getPendingMfaChallenge(),
        });
      }
    };

    void hydrate();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (state.session) {
      initializeAnalyticsTransport();
    }
  }, [state.session]);

  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  const signIn = useCallback<AuthContextValue['signIn']>(async (email, password, rememberMe = true) => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const result = await signInWithBackend({ email, password, rememberMe });
      if (result.status === 'authenticated') {
        setState({
          session: result.session,
          user: result.session.currentUser,
          loading: false,
          error: null,
          pendingMfaChallenge: null,
        });
      } else {
        setState({
          session: null,
          user: null,
          loading: false,
          error: null,
          pendingMfaChallenge: result.challenge,
        });
      }
      return result;
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: toMessage(error) }));
      throw error;
    }
  }, []);

  const signUp = useCallback<AuthContextValue['signUp']>(async (input) => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const session = await registerLearnerRequest(input);
      setState({
        session,
        user: session.currentUser,
        loading: false,
        error: null,
        pendingMfaChallenge: null,
      });
      return session;
    } catch (error) {
      setState((current) => ({ ...current, loading: false, error: toMessage(error) }));
      throw error;
    }
  }, []);

  const signOut = useCallback<AuthContextValue['signOut']>(async () => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      await signOutFromBackend();
    } finally {
      setState({
        session: null,
        user: null,
        loading: false,
        error: null,
        pendingMfaChallenge: null,
      });
    }
  }, []);

  const refreshSession = useCallback<AuthContextValue['refreshSession']>(async () => {
    setState((current) => ({ ...current, loading: true, error: null }));
    try {
      const session = await restoreSession();
      setState({
        session,
        user: session?.currentUser ?? null,
        loading: false,
        error: null,
        pendingMfaChallenge: getPendingMfaChallenge(),
      });
      return session;
    } catch (error) {
      setState({
        session: null,
        user: null,
        loading: false,
        error: toMessage(error),
        pendingMfaChallenge: getPendingMfaChallenge(),
      });
      throw error;
    }
  }, []);

  const sendVerificationOtp = useCallback<AuthContextValue['sendVerificationOtp']>(async () => {
    const email = stateRef.current.user?.email;
    if (!email) {
      throw new Error('An authenticated user email is required.');
    }
    return sendEmailVerificationOtpRequest(email);
  }, []);

  const verifyEmailOtp = useCallback<AuthContextValue['verifyEmailOtp']>(async (code) => {
    const email = stateRef.current.user?.email;
    if (!email) {
      throw new Error('An authenticated user email is required.');
    }
    const currentUser = await verifyEmailOtpRequest(email, code);
    setState((current) => ({
      ...current,
      user: currentUser,
      session: current.session ? { ...current.session, currentUser } : null,
      error: null,
    }));
    return currentUser;
  }, []);

  const beginAuthenticatorSetup = useCallback<AuthContextValue['beginAuthenticatorSetup']>(async () => {
    return beginAuthenticatorSetupRequest();
  }, []);

  const confirmAuthenticatorSetup = useCallback<AuthContextValue['confirmAuthenticatorSetup']>(async (code) => {
    const currentUser = await confirmAuthenticatorSetupRequest(code);
    setState((current) => ({
      ...current,
      user: currentUser,
      session: current.session ? { ...current.session, currentUser } : null,
      error: null,
    }));
    return currentUser;
  }, []);

  const completeMfaChallenge = useCallback<AuthContextValue['completeMfaChallenge']>(async (code) => {
    const session = await completeMfaChallengeRequest(code);
    setState({
      session,
      user: session.currentUser,
      loading: false,
      error: null,
      pendingMfaChallenge: null,
    });
    return session;
  }, []);

  const completeRecoveryChallenge = useCallback<AuthContextValue['completeRecoveryChallenge']>(async (recoveryCode) => {
    const session = await completeRecoveryChallengeRequest(recoveryCode);
    setState({
      session,
      user: session.currentUser,
      loading: false,
      error: null,
      pendingMfaChallenge: null,
    });
    return session;
  }, []);

  const clearError = useCallback<AuthContextValue['clearError']>(() => {
    setState((current) => ({ ...current, error: null }));
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    ...state,
    role: state.user?.role ?? null,
    isAuthenticated: !!state.user,
    signIn,
    signUp,
    signOut,
    refreshSession,
    sendVerificationOtp,
    verifyEmailOtp,
    beginAuthenticatorSetup,
    confirmAuthenticatorSetup,
    completeMfaChallenge,
    completeRecoveryChallenge,
    clearError,
  }), [
    state,
    signIn,
    signUp,
    signOut,
    refreshSession,
    sendVerificationOtp,
    verifyEmailOtp,
    beginAuthenticatorSetup,
    confirmAuthenticatorSetup,
    completeMfaChallenge,
    completeRecoveryChallenge,
    clearError,
  ]);

  return (
    <AuthContext.Provider value={value}>
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
