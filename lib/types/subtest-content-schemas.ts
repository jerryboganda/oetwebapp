// ── Subtest Content Productization Schemas ──
// These define the canonical JSON structure expected inside ContentItem.DetailJson
// and ContentItem.ModelAnswerJson for each OET subtest.

// ════════════════════════════════════════════════
// WRITING
// ════════════════════════════════════════════════

export interface WritingCaseNotes {
  patientName: string;
  age: string;
  gender: string;
  admissionDate?: string;
  diagnosis: string;
  history: string;
  examination?: string;
  treatment: string;
  discharge?: string;
  followUp?: string;
  nursingNotes?: string;
  pharmacyNotes?: string;
}

export interface WritingTaskDetail {
  caseNotes: WritingCaseNotes;
  taskInstructions: string;
  writingType: 'discharge_summary' | 'referral_letter' | 'transfer_letter' | 'prescription' | 'patient_note';
  wordLimit: number;
  timeLimit: number; // minutes
  professionSpecificContext?: string;
  keyPointsToAddress?: string[];
}

export interface WritingModelAnswer {
  modelResponse: string;
  annotations?: WritingAnnotation[];
  criterionBreakdown?: Record<string, { score: string; explanation: string }>;
  commonMistakes?: string[];
  alternativeApproaches?: string[];
}

export interface WritingAnnotation {
  startChar: number;
  endChar: number;
  criterion: string;
  note: string;
  quality: 'good' | 'improvement_needed' | 'error';
}

// ════════════════════════════════════════════════
// SPEAKING
// ════════════════════════════════════════════════

export interface SpeakingRoleCard {
  setting: string;
  candidateRole: string;
  patientRole: string;
  taskObjectives: string[];
  prepTimeSeconds: number;
  interactionTimeSeconds: number;
  backgroundInfo?: string;
}

export interface SpeakingInterlocutorScript {
  openingLine: string;
  cueCards: SpeakingCue[];
  closingLine?: string;
}

export interface SpeakingCue {
  trigger: string;
  response: string;
  followUpIfNeeded?: string;
}

export interface SpeakingTaskDetail {
  roleCard: SpeakingRoleCard;
  interlocutorScript?: SpeakingInterlocutorScript;
  professionSpecificVocab?: string[];
  warmupPrompts?: string[];
}

export interface SpeakingModelAnswer {
  sampleTranscript?: string;
  criterionBreakdown?: Record<string, { score: string; explanation: string }>;
  keyPhrases?: string[];
  commonErrors?: string[];
}

// ════════════════════════════════════════════════
// READING
// ════════════════════════════════════════════════

export interface ReadingPassage {
  id: string;
  text: string;
  source?: string;
  wordCount?: number;
}

export type ReadingQuestionType = 'multiple_choice' | 'gap_fill' | 'short_answer' | 'matching' | 'sentence_completion' | 'true_false_not_given';

export interface ReadingQuestion {
  id: string;
  passageRef: string;
  questionText: string;
  questionType: ReadingQuestionType;
  options?: string[];
  correctAnswer: string | string[];
  distractorExplanations?: Record<string, string>;
  skillTag?: 'inference' | 'vocabulary_in_context' | 'main_idea' | 'detail' | 'opinion' | 'reference';
}

export interface ReadingTaskDetail {
  part: 'A' | 'B' | 'C';
  passages: ReadingPassage[];
  questions: ReadingQuestion[];
  timeLimitMinutes?: number;
}

export interface ReadingModelAnswer {
  answers: Record<string, { answer: string; explanation: string }>;
  readingStrategies?: string[];
}

// ════════════════════════════════════════════════
// LISTENING
// ════════════════════════════════════════════════

export interface ListeningAudioSegment {
  id: string;
  mediaAssetId?: string;
  audioUrl?: string;
  startTime?: number; // seconds
  endTime?: number;
  speakerLabels?: string[];
  transcript?: string;
}

export type ListeningQuestionType = 'multiple_choice' | 'gap_fill' | 'short_answer' | 'matching' | 'note_completion';

export interface ListeningQuestion {
  id: string;
  audioSegmentRef: string;
  questionText: string;
  questionType: ListeningQuestionType;
  options?: string[];
  correctAnswer: string | string[];
  skillTag?: 'specific_info' | 'gist' | 'opinion' | 'speakers_purpose' | 'inference';
}

export interface ListeningTaskDetail {
  part: 'A' | 'B' | 'C';
  audioSegments: ListeningAudioSegment[];
  questions: ListeningQuestion[];
  playbackRules?: {
    maxPlays: number; // Part A: 1, Part B: 1, Part C: 1
    pauseAllowed: boolean;
  };
  timeLimitMinutes?: number;
}

export interface ListeningModelAnswer {
  answers: Record<string, { answer: string; explanation: string }>;
  transcript?: string; // full transcript for review
  listeningStrategies?: string[];
}

// ════════════════════════════════════════════════
// FOUNDATION CONTENT SCHEMAS
// ════════════════════════════════════════════════

export interface GrammarExercise {
  id: string;
  type: 'fill_blank' | 'multiple_choice' | 'rewrite' | 'error_correction' | 'matching';
  instruction: string;
  question: string;
  options?: string[];
  correctAnswer: string | string[];
  explanation?: string;
}

export interface VocabularyEntry {
  term: string;
  definition: string;
  exampleSentence?: string;
  pronunciation?: string;
  partOfSpeech?: string;
  medicalContext?: string;
}

export interface FoundationLessonDetail {
  lessonType: 'grammar' | 'vocabulary' | 'pronunciation' | 'medical_terminology';
  contentHtml?: string;
  exercises?: GrammarExercise[];
  vocabularyEntries?: VocabularyEntry[];
  prerequisiteKnowledge?: string;
  learningObjectives?: string[];
}
