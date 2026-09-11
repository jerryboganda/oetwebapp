/**
 * Candidate-facing Speaking reference data: the 9 assessment criteria and the
 * 12 introductory questions.
 *
 * The data itself lives in `data/speaking-candidate-resources.json`, not here.
 * That file is the single source of truth and the AI Learning Companion's
 * knowledge indexer reads the same one
 * (`backend/src/OetLearner.Api/Services/Companion/CompanionSpeakingCriteriaIndexer.cs`),
 * so Sami and the /speaking pages can never quote different criteria at a
 * candidate. Testing Pack 1 scenario 24 asks Sami for exactly this approved set
 * and instructs it not to invent one, which only works if there is one set.
 *
 * This module keeps the typed shape and the route constants the UI imports, so
 * every existing call site is unchanged.
 */
import data from '@/data/speaking-candidate-resources.json';

export interface LinguisticBand {
  band: number;
  descriptors: string[];
}

export interface LinguisticCriterion {
  id: string;
  name: string;
  maxBand: 6;
  bands: LinguisticBand[];
}

export interface ClinicalIndicator {
  code: string;
  text: string;
}

export interface ClinicalCriterion {
  id: string;
  letter: string;
  name: string;
  maxScore: 3;
  scale: string[];
  indicators: ClinicalIndicator[];
}

export interface SpeakingIntroQuestion {
  no: number;
  question: string;
  sampleAnswer: string;
  note?: string;
}

/** The 4 linguistic criteria, each scored 0–6. */
export const SPEAKING_LINGUISTIC_CRITERIA: LinguisticCriterion[] =
  data.linguisticCriteria as LinguisticCriterion[];

/** The 5 clinical communication criteria (A–E), each scored 0–3. */
export const SPEAKING_CLINICAL_CRITERIA: ClinicalCriterion[] =
  data.clinicalCriteria as ClinicalCriterion[];

/** 12 common introductory questions with adaptable sample answers, global/profession-neutral. */
export const SPEAKING_INTRO_QUESTIONS: SpeakingIntroQuestion[] =
  data.introQuestions as SpeakingIntroQuestion[];

export const SPEAKING_ASSESSMENT_CRITERIA_HREF = '/speaking/assessment-criteria';
export const SPEAKING_INTRO_QUESTIONS_HREF = '/speaking/intro-questions';
