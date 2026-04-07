// ── Conversation Types ─────────────────────────────────────────────────
// Derived from ConversationEntities.cs field shapes

export type ConversationState =
  | 'preparing'
  | 'active'
  | 'completed'
  | 'abandoned'
  | 'evaluating'
  | 'evaluated';

export type ConversationTurnRole = 'learner' | 'ai' | 'system';

export interface ConversationSession {
  id: string;
  userId: string;
  contentId: string | null;
  examTypeCode: string;
  subtestCode: string;
  taskTypeCode: string;
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

export interface ConversationScenario {
  title: string;
  setting: string;
  patientRole: string;
  clinicianRole: string;
  context: string;
  objectives: string[];
  timeLimit: number;           // seconds
}

export interface ConversationEvaluation {
  sessionId: string;
  overallScore: number;
  overallGrade: string;
  criterionScores: ConversationCriterionScore[];
  turnAnnotations: ConversationTurnAnnotation[];
  strengths: string[];
  improvements: string[];
  suggestions: string[];
  evaluatedAt: string;
}

export interface ConversationCriterionScore {
  criterionCode: string;
  criterionName: string;
  score: number;
  maxScore: number;
  explanation: string;
  confidenceBand: 'low' | 'medium' | 'high';
}

export interface ConversationTurnAnnotation {
  turnNumber: number;
  role: ConversationTurnRole;
  annotations: Array<{
    type: 'strength' | 'improvement' | 'error';
    text: string;
    suggestion: string | null;
  }>;
}

export interface ConversationHistory {
  items: ConversationSessionSummary[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ConversationSessionSummary {
  id: string;
  taskTypeCode: string;
  examTypeCode: string;
  state: ConversationState;
  turnCount: number;
  durationSeconds: number;
  overallScore: number | null;
  createdAt: string;
  completedAt: string | null;
}

export interface ConversationCreateRequest {
  contentId?: string;
  examFamilyCode: string;
  taskTypeCode: string;
}

// ── SignalR Hub Events ─────────────────────────────────────────────────

export interface ConversationHubEvents {
  /** Server → Client: transcript of learner audio */
  ReceiveTranscript: (turnNumber: number, text: string, confidence: number) => void;
  /** Server → Client: AI response text */
  ReceiveAIResponse: (turnNumber: number, text: string) => void;
  /** Server → Client: session state change */
  SessionStateChanged: (state: ConversationState) => void;
  /** Server → Client: error during processing */
  ConversationError: (code: string, message: string) => void;
}

export interface ConversationHubMethods {
  StartSession: (sessionId: string) => Promise<void>;
  SendAudio: (sessionId: string, audioBase64: string) => Promise<void>;
  EndSession: (sessionId: string) => Promise<void>;
}
