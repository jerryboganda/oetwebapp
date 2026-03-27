'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { type User, onAuthStateChanged, signInWithEmailAndPassword, createUserWithEmailAndPassword, signOut as firebaseSignOut } from 'firebase/auth';
import { auth, isFirebaseConfigured } from '@/lib/firebase';

const IS_DEV = process.env.NODE_ENV === 'development';

interface AuthState {
  user: User | null;
  loading: boolean;
  error: string | null;
}

interface AuthContextValue extends AuthState {
  signIn: (email: string, password: string) => Promise<void>;
  signUp: (email: string, password: string) => Promise<void>;
  signOut: () => Promise<void>;
  /** For dev only: simulate auth without Firebase */
  mockSignIn: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

// Mock user for development ONLY when Firebase is not configured
const MOCK_USER = {
  uid: 'mock-user-001',
  email: 'learner@oet-prep.dev',
  displayName: 'Faisal Maqsood',
} as User;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ user: null, loading: true, error: null });

  useEffect(() => {
    if (!isFirebaseConfigured || !auth) {
      if (IS_DEV) {
        // Development fallback: use mock auth
        setState({ user: MOCK_USER, loading: false, error: null });
      } else {
        // Production: Firebase must be configured
        setState({ user: null, loading: false, error: 'Authentication is not configured. Please contact support.' });
      }
      return;
    }

    const unsubscribe = onAuthStateChanged(
      auth,
      (user) => setState({ user, loading: false, error: null }),
      (err) => setState({ user: null, loading: false, error: err.message }),
    );
    return unsubscribe;
  }, []);

  const signIn = async (email: string, password: string) => {
    if (!auth) {
      if (IS_DEV) { setState({ user: MOCK_USER, loading: false, error: null }); return; }
      setState((s) => ({ ...s, loading: false, error: 'Authentication is not configured.' }));
      return;
    }
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      await signInWithEmailAndPassword(auth, email, password);
    } catch (err: any) {
      setState((s) => ({ ...s, loading: false, error: err.message }));
    }
  };

  const signUp = async (email: string, password: string) => {
    if (!auth) {
      if (IS_DEV) { setState({ user: MOCK_USER, loading: false, error: null }); return; }
      setState((s) => ({ ...s, loading: false, error: 'Authentication is not configured.' }));
      return;
    }
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      await createUserWithEmailAndPassword(auth, email, password);
    } catch (err: any) {
      setState((s) => ({ ...s, loading: false, error: err.message }));
    }
  };

  const signOut = async () => {
    if (!auth) {
      setState({ user: null, loading: false, error: null });
      return;
    }
    await firebaseSignOut(auth);
  };

  const mockSignIn = () => {
    if (!IS_DEV) return;
    setState({ user: MOCK_USER, loading: false, error: null });
  };

  return (
    <AuthContext.Provider value={{ ...state, signIn, signUp, signOut, mockSignIn }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
