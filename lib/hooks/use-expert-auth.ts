import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';

export interface UserRole {
  role: 'learner' | 'expert' | 'admin';
  isAuthenticated: boolean;
}

// MOCK AUTHENTICATION HOOK
// TODO: Replace with actual Firebase Auth / NextAuth implementation later
export function useExpertAuth() {
  const router = useRouter();
  const [authState, setAuthState] = useState<UserRole>({
    role: 'expert', // Defaulting to 'expert' for development, switch to 'learner' to test the protection
    isAuthenticated: true,
  });
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    // Simulate an API call to verify session and role
    const verifyAuth = async () => {
      setIsLoading(true);
      
      // Simulate network delay
      await new Promise(resolve => setTimeout(resolve, 500));
      
      // We are artificially forcing the role here. 
      // In production, this data comes from your JWT or Firebase user claims.
      const currentRole = 'expert'; 
      
      setAuthState({
        role: currentRole,
        isAuthenticated: true,
      });
      setIsLoading(false);
    };

    verifyAuth();
  }, []);

  const requireExpertAccess = () => {
    if (!isLoading) {
      if (!authState.isAuthenticated || authState.role !== 'expert') {
        // Redirect standard learners away from the expert console
        router.push('/');
      }
    }
  };

  return {
    ...authState,
    isLoading,
    requireExpertAccess,
  };
}
