'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import {
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import { TARGET_COUNTRY_OPTIONS } from '@/lib/auth/target-countries';
import type {
  ExternalAuthProvider,
  SignupCatalog,
  SignupExamType,
  SignupProfession,
} from '@/lib/types/auth';

function sanitizePublicSignupCatalog(catalog: SignupCatalog): {
  examTypes: SignupExamType[];
  professions: SignupProfession[];
  externalAuthProviders: ExternalAuthProvider[];
  targetCountryOptions: string[];
} {
  const serverExamTypes = Array.isArray(catalog.examTypes) ? catalog.examTypes : [];
  const serverProfessions = Array.isArray(catalog.professions) ? catalog.professions : [];
  const serverTargetCountryOptions = Array.isArray(catalog.targetCountryOptions) ? catalog.targetCountryOptions : [];

  return {
    examTypes: serverExamTypes.length > 0 ? serverExamTypes : fallbackExamTypes,
    professions: serverProfessions.length > 0 ? serverProfessions : fallbackProfessions,
    externalAuthProviders: Array.isArray(catalog.externalAuthProviders) ? catalog.externalAuthProviders : [],
    targetCountryOptions: serverTargetCountryOptions.length > 0 ? serverTargetCountryOptions : TARGET_COUNTRY_OPTIONS,
  };
}

export function useSignupCatalog() {
  const [examTypes, setExamTypes] = useState<SignupExamType[]>(fallbackExamTypes);
  const [professions, setProfessions] = useState<SignupProfession[]>(fallbackProfessions);
  const [externalAuthProviders, setExternalAuthProviders] = useState<ExternalAuthProvider[]>([]);
  const [targetCountryOptions, setTargetCountryOptions] = useState<readonly string[]>(TARGET_COUNTRY_OPTIONS);

  useEffect(() => {
    let cancelled = false;

    const loadCatalog = async () => {
      try {
        const catalog = await fetchSignupCatalog();

        if (cancelled) {
          return;
        }

        const publicCatalog = sanitizePublicSignupCatalog(catalog);
        setExamTypes(publicCatalog.examTypes);
        setProfessions(publicCatalog.professions);
        setExternalAuthProviders(publicCatalog.externalAuthProviders);
        setTargetCountryOptions(publicCatalog.targetCountryOptions);
      } catch {
        if (cancelled) {
          return;
        }

        setExamTypes(fallbackExamTypes);
        setProfessions(fallbackProfessions);
        setExternalAuthProviders([]);
        setTargetCountryOptions(TARGET_COUNTRY_OPTIONS);
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
    targetCountryOptions,
  };
}
