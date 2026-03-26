import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';

export interface UserRole {
  role: 'learner' | 'expert' | 'admin';
  isAuthenticated: boolean;
}

export function useAdminAuth() {
  const router = useRouter();
  const [authState, setAuthState] = useState<UserRole>({
    role: 'admin', // Defaulting to 'admin' for development
    isAuthenticated: true,
  });
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // Simulate an API call to verify session and role
    const verifyAuth = async () => {
      setIsLoading(true);
      await new Promise(resolve => setTimeout(resolve, 300));
      
      setAuthState({
        role: 'admin',
        isAuthenticated: true,
      });
      setIsLoading(false);
    };

    verifyAuth();
  }, []);

  const requireAdminAccess = () => {
    if (!isLoading) {
      if (!authState.isAuthenticated || authState.role !== 'admin') {
        router.push('/');
      }
    }
  };

  return {
    ...authState,
    isLoading,
    requireAdminAccess,
  };
}
