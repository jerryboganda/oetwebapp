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

export interface EnrollmentSession {
  id: string;
  name: string;
  examTypeId: string;
  professionIds: string[];
  priceLabel: string;
  startDate: string;
  endDate: string;
  deliveryMode: "online" | "hybrid" | "in-person";
  capacity: number;
  seatsRemaining: number;
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
    countryTargets: ["Australia", "New Zealand"],
    examTypeIds: ["oet"],
    description: "Registered nurse and clinical nursing candidates.",
  },
  {
    id: "medicine",
    label: "Medicine",
    countryTargets: ["United Kingdom", "Australia"],
    examTypeIds: ["oet"],
    description: "Doctors and physicians preparing for healthcare pathways.",
  },
  {
    id: "pharmacy",
    label: "Pharmacy",
    countryTargets: ["Ireland", "Australia"],
    examTypeIds: ["oet"],
    description: "Pharmacists and pharmacy practice candidates.",
  },
  {
    id: "dentistry",
    label: "Dentistry",
    countryTargets: ["United Kingdom", "New Zealand"],
    examTypeIds: ["oet"],
    description: "Dental professionals and dentistry applicants.",
  },
  {
    id: "academic-english",
    label: "Academic / General English",
    countryTargets: ["Canada", "United Kingdom", "Australia"],
    examTypeIds: ["ielts"],
    description: "General academic and migration IELTS candidates.",
  },
];

export const enrollmentSessions: EnrollmentSession[] = [
  {
    id: "session-oet-nursing-apr",
    name: "OET Nursing April Cohort",
    examTypeId: "oet",
    professionIds: ["nursing"],
    priceLabel: "$299",
    startDate: "2026-04-06",
    endDate: "2026-06-28",
    deliveryMode: "online",
    capacity: 40,
    seatsRemaining: 11,
  },
  {
    id: "session-oet-medicine-may",
    name: "OET Medicine Intensive",
    examTypeId: "oet",
    professionIds: ["medicine", "dentistry", "pharmacy"],
    priceLabel: "$349",
    startDate: "2026-05-11",
    endDate: "2026-07-05",
    deliveryMode: "hybrid",
    capacity: 32,
    seatsRemaining: 9,
  },
  {
    id: "session-ielts-foundation-apr",
    name: "IELTS Foundation Sprint",
    examTypeId: "ielts",
    professionIds: ["academic-english"],
    priceLabel: "$199",
    startDate: "2026-04-20",
    endDate: "2026-06-01",
    deliveryMode: "online",
    capacity: 60,
    seatsRemaining: 21,
  },
  {
    id: "session-ielts-weekend-may",
    name: "IELTS Weekend Cohort",
    examTypeId: "ielts",
    professionIds: ["academic-english"],
    priceLabel: "$239",
    startDate: "2026-05-23",
    endDate: "2026-07-19",
    deliveryMode: "online",
    capacity: 45,
    seatsRemaining: 0,
  },
];
