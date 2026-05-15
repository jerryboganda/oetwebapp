'use client';

import { useEffect, useState } from 'react';
import { fetchSignupCatalog } from '@/lib/auth-client';
import {
  examTypes as fallbackExamTypes,
  professions as fallbackProfessions,
} from '@/lib/auth/enrollment';
import type {
  ExternalAuthProvider,
  SignupCatalog,
  SignupExamType,
  SignupProfession,
} from '@/lib/types/auth';

const OET_EXAM_ID = 'oet';

function isOetExam(item: SignupExamType) {
  return item.id.toLowerCase() === OET_EXAM_ID || item.code.toLowerCase() === OET_EXAM_ID;
}

function sanitizePublicSignupCatalog(catalog: SignupCatalog): {
  examTypes: SignupExamType[];
  professions: SignupProfession[];
  externalAuthProviders: ExternalAuthProvider[];
} {
  const serverAllowsNonOetBeta = catalog.nonOetBetaEnabled === true;
  const serverExamTypes = Array.isArray(catalog.examTypes) ? catalog.examTypes : fallbackExamTypes;
  const publicExamTypes = serverAllowsNonOetBeta ? serverExamTypes : serverExamTypes.filter(isOetExam);
  const allowedExamIds = new Set(publicExamTypes.map((item) => item.id));
  const serverProfessions = Array.isArray(catalog.professions) ? catalog.professions : fallbackProfessions;
  const publicProfessions = serverAllowsNonOetBeta
    ? serverProfessions
    : serverProfessions
        .map((item) => ({
          ...item,
          examTypeIds: item.examTypeIds.filter((id) => allowedExamIds.has(id)),
        }))
        .filter((item) => item.examTypeIds.length > 0);

  return {
    examTypes: publicExamTypes.length > 0 ? publicExamTypes : fallbackExamTypes,
    professions: publicProfessions.length > 0 ? publicProfessions : fallbackProfessions,
    externalAuthProviders: Array.isArray(catalog.externalAuthProviders) ? catalog.externalAuthProviders : [],
  };
}

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

        const publicCatalog = sanitizePublicSignupCatalog(catalog);
        setExamTypes(publicCatalog.examTypes);
        setProfessions(publicCatalog.professions);
        setExternalAuthProviders(publicCatalog.externalAuthProviders);
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
