'use client';

import {
  beginAuthenticatorSetup as beginAuthenticatorSetupRequest,
  completeDeviceVerification as completeDeviceVerificationRequest,
  completeMfaChallenge as completeMfaChallengeRequest,
  completeRecoveryChallenge as completeRecoveryChallengeRequest,
  confirmAuthenticatorSetup as confirmAuthenticatorSetupRequest,
  getPendingDeviceChallenge,
  getPendingMfaChallenge,
  registerLearner as registerLearnerRequest,
  restoreSession,
  sendEmailVerificationOtp as sendEmailVerificationOtpRequest,
  signIn as signInWithBackend,
  signOut as signOutFromBackend,
  verifyEmailOtp as verifyEmailOtpRequest,
} from '@/lib/auth-client';
import { clearPendingDeviceChallenge } from '@/lib/auth-storage';
import type {
  AuthSession,
  AuthenticatorSetup,
  CurrentUser,
  MfaCompletionResult,
  OtpChallenge,
  PendingDeviceChallenge,
  PendingMfaChallenge,
  RegisterLearnerInput,
  SignInResult,
  UserRole,
} from '@/lib/types/auth';
import { initializeAnalyticsTransport } from '@/lib/analytics';
import { resetAllStores } from '@/lib/stores/registry';
import { getQueryClient } from '@/components/providers/query-provider';
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

interface AuthState {
  session: AuthSession | null;
  user: CurrentUser | null;
  loading: boolean;
  error: string | null;
  pendingMfaChallenge: PendingMfaChallenge | null;
  pendingDeviceChallenge: PendingDeviceChallenge | null;
}

export interface AuthContextValue extends AuthState {
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
  completeMfaChallenge: (code: string) => Promise<MfaCompletionResult>;
  completeRecoveryChallenge: (recoveryCode: string) => Promise<MfaCompletionResult>;
  /** Security spec §3.2: completes the device-binding email-OTP challenge
   * (`pendingDeviceChallenge`) and restores the session it was blocking. */
  completeDeviceVerification: (code: string) => Promise<AuthSession>;
  /** Abandons a device challenge so the learner can sign in with another account. */
  cancelDeviceVerification: () => void;
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
    pendingDeviceChallenge: null,
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
          pendingDeviceChallenge: getPendingDeviceChallenge(),
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
          pendingDeviceChallenge: getPendingDeviceChallenge(),
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

  const value = useMemo<AuthContextValue>(() => ({
    ...state,
    role: state.user?.role ?? null,
    isAuthenticated: !!state.user,
    async signIn(email, password, rememberMe = true) {
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
            pendingDeviceChallenge: null,
          });
        } else if (result.status === 'mfa_required') {
          setState({
            session: null,
            user: null,
            loading: false,
            error: null,
            pendingMfaChallenge: result.challenge,
            pendingDeviceChallenge: null,
          });
        } else {
          setState({
            session: null,
            user: null,
            loading: false,
            error: null,
            pendingMfaChallenge: null,
            pendingDeviceChallenge: result.challenge,
          });
        }

        return result;
      } catch (error) {
        setState((current) => ({
          ...current,
          loading: false,
          error: toMessage(error),
        }));
        throw error;
      }
    },
    async signUp(input) {
      setState((current) => ({ ...current, loading: true, error: null }));

      try {
        const session = await registerLearnerRequest(input);
        setState({
          session,
          user: session.currentUser,
          loading: false,
          error: null,
          pendingMfaChallenge: null,
          pendingDeviceChallenge: null,
        });
        return session;
      } catch (error) {
        setState((current) => ({
          ...current,
          loading: false,
          error: toMessage(error),
        }));
        throw error;
      }
    },
    async signOut() {
      setState((current) => ({ ...current, loading: true, error: null }));

      try {
        await signOutFromBackend();
      } finally {
        // FE-001: wipe ALL client-side state on logout so a shared device never
        // leaks the previous user's cached data or persisted review drafts.
        // getQueryClient() returns the same browser singleton the provider uses.
        getQueryClient().clear();
        resetAllStores();
        setState({
          session: null,
          user: null,
          loading: false,
          error: null,
          pendingMfaChallenge: null,
          pendingDeviceChallenge: null,
        });
      }
    },
    async refreshSession() {
      setState((current) => ({ ...current, loading: true, error: null }));

      try {
        const session = await restoreSession();
        setState({
          session,
          user: session?.currentUser ?? null,
          loading: false,
          error: null,
          pendingMfaChallenge: getPendingMfaChallenge(),
          pendingDeviceChallenge: getPendingDeviceChallenge(),
        });
        return session;
      } catch (error) {
        setState({
          session: null,
          user: null,
          loading: false,
          error: toMessage(error),
          pendingMfaChallenge: getPendingMfaChallenge(),
          pendingDeviceChallenge: getPendingDeviceChallenge(),
        });
        throw error;
      }
    },
    async sendVerificationOtp() {
      const email = state.user?.email;
      if (!email) {
        throw new Error('An authenticated user email is required.');
      }

      return sendEmailVerificationOtpRequest(email);
    },
    async verifyEmailOtp(code) {
      const email = state.user?.email;
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
    },
    async beginAuthenticatorSetup() {
      return beginAuthenticatorSetupRequest();
    },
    async confirmAuthenticatorSetup(code) {
      const currentUser = await confirmAuthenticatorSetupRequest(code);
      setState((current) => ({
        ...current,
        user: currentUser,
        session: current.session ? { ...current.session, currentUser } : null,
        error: null,
      }));
      return currentUser;
    },
    async completeMfaChallenge(code) {
      const result = await completeMfaChallengeRequest(code);
      if (result.status === 'device_verification_required') {
        setState((current) => ({
          ...current,
          loading: false,
          error: null,
          pendingMfaChallenge: null,
          pendingDeviceChallenge: result.challenge,
        }));
        return result;
      }

      setState({
        session: result.session,
        user: result.session.currentUser,
        loading: false,
        error: null,
        pendingMfaChallenge: null,
        pendingDeviceChallenge: null,
      });
      return result;
    },
    async completeRecoveryChallenge(recoveryCode) {
      const result = await completeRecoveryChallengeRequest(recoveryCode);
      if (result.status === 'device_verification_required') {
        setState((current) => ({
          ...current,
          loading: false,
          error: null,
          pendingMfaChallenge: null,
          pendingDeviceChallenge: result.challenge,
        }));
        return result;
      }

      setState({
        session: result.session,
        user: result.session.currentUser,
        loading: false,
        error: null,
        pendingMfaChallenge: null,
        pendingDeviceChallenge: null,
      });
      return result;
    },
    async completeDeviceVerification(code) {
      const session = await completeDeviceVerificationRequest(code);
      setState({
        session,
        user: session.currentUser,
        loading: false,
        error: null,
        pendingMfaChallenge: null,
        pendingDeviceChallenge: null,
      });
      return session;
    },
    cancelDeviceVerification() {
      clearPendingDeviceChallenge();
      setState((current) => ({
        ...current,
        pendingDeviceChallenge: null,
        error: null,
      }));
    },
    clearError() {
      setState((current) => ({ ...current, error: null }));
    },
  }), [state]);

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
