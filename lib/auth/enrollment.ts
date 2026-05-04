import { TARGET_COUNTRY_OPTIONS } from './target-countries';

export interface ExamType {
  id: string;
  label: string;
  code: string;
  description: string;
}

export interface Profession {
  id: string;
  label: string;
  countryTargets: string[];
  examTypeIds: string[];
  description: string;
}

export const examTypes: ExamType[] = [
  {
    id: "oet",
    label: "OET",
    code: "OET",
    description: "Occupational English Test preparation and enrollment.",
  },
  {
    id: "ielts",
    label: "IELTS",
    code: "IELTS",
    description: "IELTS preparation and session enrollment.",
  },
];

export const professions: Profession[] = [
  {
    id: "nursing",
    label: "Nursing",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Registered nurse and clinical nursing candidates.",
  },
  {
    id: "medicine",
    label: "Medicine",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Doctors and physicians preparing for healthcare pathways.",
  },
  {
    id: "pharmacy",
    label: "Pharmacy",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Pharmacists and pharmacy practice candidates.",
  },
  {
    id: "dentistry",
    label: "Dentistry",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Dental professionals and dentistry applicants.",
  },
  {
    id: "physiotherapy",
    label: "Physiotherapy",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Physiotherapists and physical therapy candidates.",
  },
  {
    id: "other-allied-health",
    label: "Other Allied health profession",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["oet"],
    description: "Other allied health professionals (occupational therapy, dietetics, speech pathology, podiatry, optometry, radiography, etc.).",
  },
  {
    id: "academic-english",
    label: "Academic / General English",
    countryTargets: [...TARGET_COUNTRY_OPTIONS],
    examTypeIds: ["ielts"],
    description: "General academic and migration IELTS candidates.",
  },
];
