export type ConversationState =
  | 'preparing' | 'active' | 'completed' | 'abandoned'
  | 'evaluating' | 'evaluated' | 'failed';

export type ConversationTurnRole = 'learner' | 'ai' | 'system';

export interface ConversationSession {
  id: string;
  userId: string;
  contentId: string | null;
  templateId: string | null;
  examTypeCode: string;
  subtestCode: string;
  taskTypeCode: string;
  profession: string;
  scenarioJson: string;
  state: ConversationState;
  turnCount: number;
  durationSeconds: number;
  transcriptJson: string;
  evaluationId: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ConversationPatientVoice {
  gender?: 'male' | 'female' | 'neutral';
  age?: number;
  accent?: string;
  tone?: string;
  voiceId?: string;
}

export interface ConversationScenario {
  templateId?: string;
  title: string;
  taskTypeCode?: string;
  profession?: string;
  difficulty?: string;
  setting?: string;
  patientRole?: string;
  clinicianRole?: string;
  context?: string;
  expectedOutcomes?: string | null;
  objectives?: string[];
  expectedRedFlags?: string[];
  keyVocabulary?: string[];
  patientVoice?: ConversationPatientVoice;
  timeLimit?: number;
  timeLimitSeconds?: number;
}

export interface ConversationEvaluationCriterion {
  id: string;
  score06: number;
  maxScore: number;
  evidence: string;
  quotes?: string[];
}

export interface ConversationEvaluationTurn {
  turnNumber: number;
  role: ConversationTurnRole;
  content: string;
  audioUrl: string | null;
  durationMs: number;
  confidence: number | null;
}

export interface ConversationEvaluationAnnotation {
  id: string;
  turnNumber: number;
  type: 'strength' | 'error' | 'improvement';
  category?: string;
  ruleId?: string | null;
  evidence: string;
  suggestion: string | null;
}

export interface ConversationEvaluationResponse {
  sessionId: string;
  state: ConversationState;
  ready: boolean;
  message?: string;
  scaledScore?: number;
  scaledMax?: number;
  passScaled?: number;
  passed?: boolean;
  overallGrade?: string;
  criteria?: ConversationEvaluationCriterion[];
  turnAnnotations?: ConversationEvaluationAnnotation[];
  turns?: ConversationEvaluationTurn[];
  strengths?: string[];
  improvements?: string[];
  suggestedPractice?: string[];
  appliedRuleIds?: string[];
  rulebookVersion?: string;
  advisory?: string;
  turnCount?: number;
  durationSeconds?: number;
  evaluatedAt?: string;
}

export interface ConversationHistoryItem {
  id: string;
  taskTypeCode: string;
  examTypeCode: string;
  profession?: string;
  state: ConversationState;
  turnCount: number;
  durationSeconds: number;
  scaledScore: number | null;
  overallGrade: string | null;
  passed: boolean | null;
  createdAt: string;
  completedAt: string | null;
}

export interface ConversationTaskTypeDescriptor {
  code: string;
  label: string;
  description: string;
}

export interface ConversationTaskTypeCatalog {
  taskTypes: ConversationTaskTypeDescriptor[];
  prepDurationSeconds: number;
  maxSessionDurationSeconds: number;
  maxTurnDurationSeconds: number;
}

export interface ConversationEntitlement {
  allowed: boolean;
  tier: string;
  remaining: number;
  limit: number;
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

export interface ConversationAiMeta {
  audioUrl: string | null;
  emotionHint: string | null;
  appliedRuleIds: string[];
}

export interface ConversationTranscriptMeta {
  audioUrl: string | null;
}
