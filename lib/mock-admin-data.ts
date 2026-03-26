// Admin mock data engine

export type ContentStatus = 'draft' | 'published' | 'archived';
export type SubTest = 'Reading' | 'Listening' | 'Writing' | 'Speaking';

export interface AdminContentItem {
  id: string;
  title: string;
  type: string;
  profession: string;
  status: ContentStatus;
  updatedAt: string;
  author: string;
  revisionCount: number;
}

export const mockContentLibrary: AdminContentItem[] = [
  { id: 'CNT-001', title: 'Cardiology Referral Letter', type: 'Writing Task', profession: 'Medicine', status: 'published', updatedAt: '2026-03-24T10:00:00Z', author: 'Dr. Smith', revisionCount: 3 },
  { id: 'CNT-002', title: 'Asthma Patient History', type: 'Speaking Roleplay', profession: 'Nursing', status: 'draft', updatedAt: '2026-03-25T14:30:00Z', author: 'Admin User', revisionCount: 1 },
  { id: 'CNT-003', title: 'Pediatric Dosage Guidelines', type: 'Reading Part A', profession: 'All', status: 'published', updatedAt: '2026-03-20T09:15:00Z', author: 'Editor Team', revisionCount: 5 },
  { id: 'CNT-004', title: 'Tooth Extraction Protocol', type: 'Writing Task', profession: 'Dentistry', status: 'archived', updatedAt: '2025-11-10T11:00:00Z', author: 'Dr. Jones', revisionCount: 2 },
  { id: 'CNT-005', title: 'Emergency Room Triage', type: 'Listening Part C', profession: 'All', status: 'published', updatedAt: '2026-03-22T16:45:00Z', author: 'Editor Team', revisionCount: 4 },
];

export interface AdminTaxonomyNode {
  id: string;
  label: string;
  slug: string;
  type: 'profession' | 'category';
  status: 'active' | 'archived';
  contentCount: number;
}

export const mockTaxonomy: AdminTaxonomyNode[] = [
  { id: 'TAX-001', label: 'Medicine', slug: 'medicine', type: 'profession', status: 'active', contentCount: 145 },
  { id: 'TAX-002', label: 'Nursing', slug: 'nursing', type: 'profession', status: 'active', contentCount: 210 },
  { id: 'TAX-003', label: 'Dentistry', slug: 'dentistry', type: 'profession', status: 'active', contentCount: 45 },
  { id: 'TAX-004', label: 'Pharmacy', slug: 'pharmacy', type: 'profession', status: 'active', contentCount: 32 },
  { id: 'TAX-005', label: 'Physiotherapy', slug: 'physiotherapy', type: 'profession', status: 'active', contentCount: 28 },
  { id: 'TAX-006', label: 'Veterinary Science', slug: 'veterinary', type: 'profession', status: 'archived', contentCount: 0 },
];

export interface AdminUser {
  id: string;
  name: string;
  email: string;
  role: 'learner' | 'expert' | 'admin';
  status: 'active' | 'suspended';
  lastLogin: string;
}

export const mockUsers: AdminUser[] = [
  { id: 'USR-001', name: 'Alice Smith', email: 'alice@example.com', role: 'learner', status: 'active', lastLogin: '2026-03-25T08:00:00Z' },
  { id: 'USR-002', name: 'Dr. Bob Jones', email: 'bob@oet-prep.dev', role: 'expert', status: 'active', lastLogin: '2026-03-25T10:15:00Z' },
  { id: 'USR-003', name: 'Admin User', email: 'admin@oet-prep.dev', role: 'admin', status: 'active', lastLogin: '2026-03-26T01:00:00Z' },
  { id: 'USR-004', name: 'Charlie Brown', email: 'charlie@example.com', role: 'learner', status: 'suspended', lastLogin: '2026-02-15T14:20:00Z' },
];

export interface AdminFeatureFlag {
  id: string;
  name: string;
  key: string;
  enabled: boolean;
  type: 'release' | 'experiment' | 'operational';
  rolloutPercentage: number;
  description: string;
  owner: string;
}

export const mockFlags: AdminFeatureFlag[] = [
  { id: 'FLG-001', name: 'New Speaking UI', key: 'speaking_ui_v2', enabled: true, type: 'release', rolloutPercentage: 100, description: 'Redesigned speaking practice interface with improved roleplay flow.', owner: 'Product Team' },
  { id: 'FLG-002', name: 'AI Strict Thresholds', key: 'ai_strict_mode', enabled: false, type: 'experiment', rolloutPercentage: 0, description: 'Tightened confidence thresholds for AI grading to reduce false positives.', owner: 'AI Team' },
  { id: 'FLG-003', name: 'Stripe Billing Sync', key: 'billing_sync', enabled: true, type: 'operational', rolloutPercentage: 100, description: 'Real-time billing sync between platform and Stripe.', owner: 'Billing Team' },
  { id: 'FLG-004', name: 'Dark Mode Beta', key: 'dark_mode_beta', enabled: true, type: 'experiment', rolloutPercentage: 20, description: 'Opt-in dark mode for learner dashboard.', owner: 'Design Team' },
];

export interface AdminAuditLog {
  id: string;
  timestamp: string;
  actor: string;
  action: string;
  resource: string;
  details: string;
}

export const mockAuditLogs: AdminAuditLog[] = [
  { id: 'AUD-001', timestamp: '2026-03-25T14:35:00Z', actor: 'Admin User', action: 'Published', resource: 'CNT-001', details: 'Status changed from draft to published' },
  { id: 'AUD-002', timestamp: '2026-03-25T12:10:00Z', actor: 'System', action: 'Threshold Updated', resource: 'AI-Config', details: 'Updated writing confidence threshold to 0.85' },
  { id: 'AUD-003', timestamp: '2026-03-24T09:00:00Z', actor: 'Admin User', action: 'Suspended User', resource: 'USR-004', details: 'Suspended due to TOS violation' },
  { id: 'AUD-004', timestamp: '2026-03-23T16:20:00Z', actor: 'Admin User', action: 'Flag Toggled', resource: 'Flag-002', details: 'Disabled AI Strict Thresholds experiment' },
];

export interface AdminCriteria {
  id: string;
  name: string;
  type: 'writing' | 'speaking';
  weight: number;
  status: 'active' | 'archived';
  description: string;
}

export const mockCriteria: AdminCriteria[] = [
  { id: 'CRI-001', name: 'Purpose', type: 'writing', weight: 3, status: 'active', description: 'Is the purpose of the document clear?' },
  { id: 'CRI-002', name: 'Content', type: 'writing', weight: 7, status: 'active', description: 'Are all required details included?' },
  { id: 'CRI-003', name: 'Conciseness & Clarity', type: 'writing', weight: 7, status: 'active', description: 'Is the language clear and concise?' },
  { id: 'CRI-004', name: 'Genre & Style', type: 'writing', weight: 7, status: 'active', description: 'Is the tone appropriate for the profession?' },
  { id: 'CRI-005', name: 'Organization & Layout', type: 'writing', weight: 7, status: 'active', description: 'Is the structure logical?' },
  { id: 'CRI-006', name: 'Language', type: 'writing', weight: 7, status: 'active', description: 'Grammar, vocabulary, and punctuation' },
  { id: 'CRI-007', name: 'Intelligibility', type: 'speaking', weight: 6, status: 'active', description: 'Pronunciation, intonation, rhythm' },
  { id: 'CRI-008', name: 'Fluency', type: 'speaking', weight: 6, status: 'active', description: 'Smoothness of speech, hesitations' },
  { id: 'CRI-009', name: 'Appropriateness of Language', type: 'speaking', weight: 6, status: 'active', description: 'Professional tone and vocabulary' },
  { id: 'CRI-010', name: 'Resources of Grammar and Expression', type: 'speaking', weight: 6, status: 'active', description: 'Accuracy and range of grammar' },
  { id: 'CRI-011', name: 'Clinical Communication Skills', type: 'speaking', weight: 6, status: 'active', description: 'Relationship-building, information gathering, empathy' },
];

export interface AdminAIConfig {
  id: string;
  model: string;
  provider: string;
  taskType: string;
  status: 'active' | 'testing' | 'deprecated';
  accuracy: number;
  confidenceThreshold: number;
  routingRule: string;
  experimentFlag: string | null;
  promptLabel: string;
}

export const mockAIConfigs: AdminAIConfig[] = [
  { id: 'AIC-001', model: 'gpt-4o', provider: 'OpenAI', taskType: 'Writing Grading', status: 'active', accuracy: 0.94, confidenceThreshold: 0.85, routingRule: 'Auto-grade if confidence ≥ threshold, else route to expert', experimentFlag: null, promptLabel: 'writing-grade-v4.2' },
  { id: 'AIC-002', model: 'claude-3-5-sonnet', provider: 'Anthropic', taskType: 'Speaking Analysis', status: 'testing', accuracy: 0.91, confidenceThreshold: 0.80, routingRule: 'Always route to expert review after AI pre-score', experimentFlag: 'ai_strict_mode', promptLabel: 'speaking-analysis-v2.1' },
  { id: 'AIC-003', model: 'gpt-3.5-turbo', provider: 'OpenAI', taskType: 'Grammar Check', status: 'deprecated', accuracy: 0.82, confidenceThreshold: 0.70, routingRule: 'Inline suggestion only, no scoring', experimentFlag: null, promptLabel: 'grammar-check-v1.0' },
  { id: 'AIC-004', model: 'whisper-1', provider: 'OpenAI', taskType: 'Speech-to-Text', status: 'active', accuracy: 0.96, confidenceThreshold: 0.90, routingRule: 'Transcribe all audio, flag low-confidence segments', experimentFlag: null, promptLabel: 'stt-whisper-v1.3' },
];

export interface AdminReviewOp {
  id: string;
  taskId: string;
  learnerId: string;
  expertId: string;
  status: 'pending' | 'in_progress' | 'completed';
  assignedAt: string;
  priority: 'high' | 'normal' | 'low';
}

export const mockReviewOps: AdminReviewOp[] = [
  { id: 'REV-001', taskId: 'CNT-001', learnerId: 'USR-001', expertId: 'USR-002', status: 'pending', assignedAt: '2026-03-25T09:00:00Z', priority: 'high' },
  { id: 'REV-002', taskId: 'CNT-002', learnerId: 'USR-004', expertId: 'USR-002', status: 'in_progress', assignedAt: '2026-03-24T14:30:00Z', priority: 'normal' },
  { id: 'REV-003', taskId: 'CNT-001', learnerId: 'USR-001', expertId: 'Unassigned', status: 'pending', assignedAt: '2026-03-26T10:15:00Z', priority: 'normal' },
];

export interface AdminBillingPlan {
  id: string;
  name: string;
  price: number;
  interval: 'month' | 'year';
  activeSubscribers: number;
  status: 'active' | 'legacy';
}

export const mockBillingPlans: AdminBillingPlan[] = [
  { id: 'PLAN-001', name: 'Basic Monthly', price: 29, interval: 'month', activeSubscribers: 1250, status: 'active' },
  { id: 'PLAN-002', name: 'Pro Monthly', price: 49, interval: 'month', activeSubscribers: 840, status: 'active' },
  { id: 'PLAN-003', name: 'Annual Premium', price: 490, interval: 'year', activeSubscribers: 320, status: 'active' },
  { id: 'PLAN-004', name: 'Early Bird (Legacy)', price: 19, interval: 'month', activeSubscribers: 115, status: 'legacy' },
];

/* ─── Review Ops KPI Data ─── */
export interface ReviewOpsKPIs {
  backlog: number;
  overdue: number;
  slaRisk: number;
  statusDistribution: { pending: number; inProgress: number; completed: number };
}

export const mockReviewOpsKPIs: ReviewOpsKPIs = {
  backlog: 24,
  overdue: 3,
  slaRisk: 7,
  statusDistribution: { pending: 14, inProgress: 10, completed: 156 },
};

/* ─── Quality Analytics Data ─── */
export interface QualityAnalyticsData {
  aiHumanAgreement: { value: number; trend: number };
  appealsRate: { value: number; trend: number };
  avgReviewTime: { value: number; unit: string };
  contentPerformance: { topContent: string; avgScore: number };
  reviewSLA: { metPercent: number; avgTurnaround: string };
  featureAdoption: { activeUsers: number; adoptionRate: number };
  riskCases: { count: number; severity: string };
}

export const mockQualityAnalytics: QualityAnalyticsData = {
  aiHumanAgreement: { value: 94.2, trend: 1.2 },
  appealsRate: { value: 2.8, trend: -0.4 },
  avgReviewTime: { value: 14.5, unit: 'min' },
  contentPerformance: { topContent: 'Writing Tasks', avgScore: 7.2 },
  reviewSLA: { metPercent: 96.1, avgTurnaround: '18h' },
  featureAdoption: { activeUsers: 2340, adoptionRate: 78.5 },
  riskCases: { count: 5, severity: 'medium' },
};

/* ─── Billing Invoices ─── */
export interface AdminBillingInvoice {
  id: string;
  userId: string;
  userName: string;
  amount: number;
  status: 'paid' | 'pending' | 'failed' | 'refunded';
  date: string;
  plan: string;
}

export const mockBillingInvoices: AdminBillingInvoice[] = [
  { id: 'INV-001', userId: 'USR-001', userName: 'Alice Smith', amount: 49, status: 'paid', date: '2026-03-25T00:00:00Z', plan: 'Pro Monthly' },
  { id: 'INV-002', userId: 'USR-004', userName: 'Charlie Brown', amount: 29, status: 'failed', date: '2026-03-20T00:00:00Z', plan: 'Basic Monthly' },
  { id: 'INV-003', userId: 'USR-001', userName: 'Alice Smith', amount: 49, status: 'paid', date: '2026-02-25T00:00:00Z', plan: 'Pro Monthly' },
  { id: 'INV-004', userId: 'USR-004', userName: 'Charlie Brown', amount: 15, status: 'refunded', date: '2026-02-15T00:00:00Z', plan: 'Basic Monthly' },
];

/* ─── Content Revisions ─── */
export interface AdminContentRevision {
  id: string;
  contentId: string;
  date: string;
  author: string;
  state: ContentStatus;
  note: string;
}

export const mockContentRevisions: AdminContentRevision[] = [
  { id: 'v3', contentId: 'CNT-001', date: '2026-03-24T10:00:00Z', author: 'Dr. Smith', state: 'published', note: 'Updated model answer.' },
  { id: 'v2', contentId: 'CNT-001', date: '2026-03-20T14:15:00Z', author: 'Admin User', state: 'draft', note: 'Drafting new case notes.' },
  { id: 'v1', contentId: 'CNT-001', date: '2026-03-10T09:00:00Z', author: 'Dr. Smith', state: 'archived', note: 'Initial creation.' },
];
