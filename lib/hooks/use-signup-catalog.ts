'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import {
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import type {
  ExternalAuthProvider,
  SignupExamType,
  SignupProfession,
} from '@/lib/types/auth';

export function useSignupCatalog() {
  const [examTypes, setExamTypes] = useState<SignupExamType[]>(fallbackExamTypes);
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [externalAuthProviders, setExternalAuthProviders] = useState<ExternalAuthProvider[]>([]);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      try {
        const catalog = await fetchSignupCatalog();

        if (cancelled) {
          return;
        }

        setExamTypes(Array.isArray(catalog.examTypes) ? catalog.examTypes : fallbackExamTypes);
        setProfessions(Array.isArray(catalog.professions) ? catalog.professions : fallbackProfessions);
        setExternalAuthProviders(Array.isArray(catalog.externalAuthProviders) ? catalog.externalAuthProviders : []);
      } catch {
        if (cancelled) {
          return;
        }

        setExamTypes(fallbackExamTypes);
        setProfessions(fallbackProfessions);
        setExternalAuthProviders([]);
      }
    };

    void loadCatalog();

    return () => {
      cancelled = true;
    };
  }, []);

  return {
    examTypes,
    externalAuthProviders,
    professions,
  };
}
