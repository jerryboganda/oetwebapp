// ── Conversation Types ─────────────────────────────────────────────────
// Derived from ConversationEntities.cs field shapes

export type ConversationState =
  | 'preparing'
  | 'active'
  | 'completed'
  | 'abandoned'
  | 'evaluating'
  | 'evaluated'
  | 'failed';

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

export interface ConversationTurn {
  id: string;
  sessionId: string;
  turnNumber: number;
  role: ConversationTurnRole;
  content: string;
  audioUrl: string | null;
  durationMs: number;
  timestampMs: number;
  confidenceScore: number | null;
  analysisJson: string;
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
  timeLimit?: number;            // legacy — seconds
  timeLimitSeconds?: number;     // canonical
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

export interface ConversationHistoryResponse {
  items: ConversationHistoryItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ConversationCreateRequest {
  contentId?: string;
  examFamilyCode?: string;
  taskTypeCode: string;
  profession?: string;
  difficulty?: string;
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
  remaining: number;  // -1 = unlimited
  limit: number;      // -1 = unlimited
  windowDays: number;
  resetAt: string | null;
  reason: string;
}

// ── SignalR Hub Events ─────────────────────────────────────────────────

export interface ConversationAiMeta {
  audioUrl: string | null;
  emotionHint: string | null;
  appliedRuleIds: string[];
}

export interface ConversationTranscriptMeta {
  audioUrl: string | null;
}

export interface ConversationHubEvents {
  /** Server → Client: transcript of learner audio */
  ReceiveTranscript: (turnNumber: number, text: string, confidence: number, meta?: ConversationTranscriptMeta) => void;
  /** Server → Client: AI response text (plus optional audio + meta) */
  ReceiveAIResponse: (turnNumber: number, text: string, meta?: ConversationAiMeta) => void;
  /** Server → Client: session state change */
  SessionStateChanged: (state: ConversationState) => void;
  /** Server → Client: server is suggesting the learner should end the session */
  SessionShouldEnd: (remainingSeconds: number) => void;
  /** Server → Client: error during processing */
  ConversationError: (code: string, message: string) => void;
}

export interface ConversationHubMethods {
  StartSession: (sessionId: string) => Promise<void>;
  SendAudio: (sessionId: string, audioBase64: string, mimeType?: string) => Promise<void>;
  EndSession: (sessionId: string) => Promise<void>;
}
