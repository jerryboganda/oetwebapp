// The ONE place that names what the Writing production QA harness talks to: hosts, API paths, app routes,
// provider ids, feature-flag keys, DOM selectors and the test-id contract the Writing launch tickets
// (WAI-07/08/09) add. Anything the product renames is changed here and in docs/qa/WRITING-PROD-QA.md only.
// No imports: geometry.mjs, the Playwright detector spec and the unit tests load this file as-is.

export const APP_URL = 'https://app.oetwithdrhesham.co.uk';
export const API_URL = 'https://api.oetwithdrhesham.co.uk';

// The 12 handoff professions under the product's own ids (lib/writing/types.ts WRITING_PROFESSIONS without
// 'other'). Which of them is ENABLED is decided live by discovery, never by this list.
export const HANDOFF_PROFESSIONS = [
  'medicine', 'nursing', 'dentistry', 'pharmacy', 'physiotherapy', 'radiography',
  'dietetics', 'occupational-therapy', 'optometry', 'podiatry', 'speech-pathology', 'veterinary',
];
// Spellings other vocabularies use for the same profession (normalised: lower case, '_'/space to '-').
export const PROFESSION_ALIASES = { 'veterinary-science': 'veterinary', vet: 'veterinary', 'speech-therapy': 'speech-pathology' };

// Script categories (handoff: Routine / Urgent / Discharge-or-follow-up) and the catalogue letter type each
// one targets first; the fallbacks are tried in order when a profession has no eligible task of that type.
export const CATEGORIES = ['routine', 'urgent', 'discharge'];
export const CATEGORY_LETTER_TYPE = { routine: 'LT-RR', urgent: 'LT-UR', discharge: 'LT-DG' };
export const FALLBACK_LETTER_TYPES = {
  routine: ['LT-NM', 'LT-OT', 'LT-TR'],
  urgent: ['LT-OT', 'LT-TR', 'LT-NM'],
  discharge: ['LT-TR', 'LT-OT', 'LT-NM'],
};

// Writing grading chain (WritingSubscriptionProviders): L1 Claude Max subscription, L2 Anthropic API (paid),
// L3 Codex subscription. Circuit keys are the same ids.
export const PROVIDERS = { claude: 'writing-claude-sub', api: 'anthropic', codex: 'writing-codex-sub' };
export const GRADE_FEATURE = 'writing.grade';
export const EMPTY_MODEL = 'deterministic-empty-v1';
// WAI-05 QA-only fault switch: FeatureFlag key prefix + learner user id; RolloutPercentage = failing runs.
export const FAULT_FLAGS = { all: 'writing_grade_fault:', l1l2: 'writing_grade_fault_l1l2:' };
export const FREE_SAMPLES_FLAG = 'free_samples_enabled';
export const LEDGER = { debit: 'GradingDeduct', refund: 'RefundOnFailure', startPrefix: 'writing-v2:', gradePrefix: 'writing-grade:' };
export const CREDITS_PER_LETTER = 2;

export const ENDPOINTS = {
  signIn: '/v1/auth/sign-in',
  professionCatalog: '/v1/professions/catalog',
  catalogueCompatibility: '/v1/admin/writing/tasks/catalogue-compatibility',
  loadIntegrity: '/v1/admin/writing/tasks/load-integrity',
  writingOptions: '/v1/admin/writing/options',
  flags: '/v1/admin/flags',
  flag: (id) => `/v1/admin/flags/${encodeURIComponent(id)}`,
  flagDeactivate: (id) => `/v1/admin/flags/${encodeURIComponent(id)}/deactivate`,
  providers: '/v1/admin/ai/providers',
  writingProvider: '/v1/admin/ai/writing-provider',
  circuits: '/v1/admin/ai/circuits',
  circuitReset: (key) => `/v1/admin/ai/circuits/${encodeURIComponent(key)}/reset?kind=provider`,
  usage: (query) => `/v1/admin/ai/usage?${new URLSearchParams({ featureCode: GRADE_FEATURE, pageSize: '200', ...query })}`,
  users: '/v1/admin/users',
  userDelete: (id) => `/v1/admin/users/${encodeURIComponent(id)}/delete`,
  adminCredits: (id) => `/v1/admin/ai-package-credits/${encodeURIComponent(id)}?pageSize=200`,
  adminCreditsAdjust: (id) => `/v1/admin/ai-package-credits/${encodeURIComponent(id)}/adjust`,
  // Learner (called from inside the signed-in page through the app's /api/backend proxy).
  entitlement: '/v1/writing/entitlement',
  myCredits: '/v1/me/ai-package-credits?pageSize=200',
  myWork: '/v1/writing/my-work?limit=50',
  freeSamples: '/v1/free-samples/writing',
  draft: (scenarioId, mode = 'practice') => `/v1/writing/drafts/${scenarioId}/${mode}`,
  submission: (id) => `/v1/writing/submissions/${id}`,
  grade: (id) => `/v1/writing/submissions/${id}/grade`,
  assessment: (id) => `/v1/writing/submissions/${id}/assessment-v11`,
  retryGrade: (id) => `/v1/writing/submissions/${id}/retry-grade`,
  submissions: '/v1/writing/submissions',
  eligibility: (scenarioId) => `/v1/writing/scenarios/${scenarioId}/eligibility`,
  scenario: (scenarioId) => `/v1/writing/scenarios/${scenarioId}`,
};
export const BROWSER_API_PREFIX = '/api/backend';

export const ROUTES = {
  signIn: '/sign-in',
  library: '/writing/practice/library',
  practice: (scenarioId) => `/writing/practice/session/${scenarioId}`,
  grading: (id) => `/writing/submissions/${id}/grading`,
  results: (id) => `/writing/submissions/${id}/results`,
  postSubmissions: '/submissions?subtest=writing',
};

// Test-id contract (plan section WAI-10). The harness refuses a suite whose ids are missing on the live DOM.
export const TEST_IDS = {
  editor: 'writing-editor',
  timer: 'writing-timer', // data-phase, data-seconds-remaining
  draftStatus: 'writing-draft-status', // data-state = saved|saving|pending-local|offline|error
  submit: 'writing-submit',
  gradingSteps: 'writing-grading-steps',
  gradingFailed: 'writing-grading-failed',
  gradingRetry: 'writing-grading-retry',
  postSubmissionsList: 'post-submissions-list',
  postSubmissionRow: 'post-submission-row', // data-submission-id, data-scenario-id, data-state
  postSubmissionOpen: 'post-submission-open',
  postSubmissionResume: 'post-submission-resume',
  postSubmissionRetry: 'post-submission-retry',
  scorePanel: 'results-score-panel',
  scoreStat: 'results-score-stat',
  gradeValue: 'grade-value',
  resultSection: 'result-section', // data-section
  correctionsPreview: 'corrections-preview',
  correctionsViewAll: 'corrections-view-all',
  correctionsFullList: 'corrections-full-list',
  shellHandle: 'shell-controls-handle',
  estimatedScore: 'ai-estimated-score',
  modelAnswer: 'grounded-model-answer',
  criteriaList: 'criteria-list', // exactly 6 li
  freeSampleReviseCta: 'free-sample-revise-cta',
  // Native-shell-only entries of the mobile menu (below lg the floating handle is hidden): the phone-width
  // positive control of the native-shell emulation.
  menuReloadApp: 'mobile-menu-reload-app',
  menuCheckUpdates: 'mobile-menu-check-updates',
  resumeBanner: 'resume-writing-banner',
};
export const CORRECTIONS_PREVIEW = 5;
export const LG_MIN_WIDTH = 1024;
export const CONTRACT_GROUPS = {
  editor: [TEST_IDS.editor, TEST_IDS.timer, TEST_IDS.draftStatus, TEST_IDS.submit],
  // The step list renders only while grading runs; a failed run shows the failure card + Retry instead.
  grading: [TEST_IDS.gradingSteps],
  gradingFailure: [TEST_IDS.gradingFailed, TEST_IDS.gradingRetry],
  postSubmissions: [TEST_IDS.postSubmissionsList, TEST_IDS.postSubmissionRow],
  // corrections-preview / -view-all exist only above 5 errors, so they are checked per report, not here.
  results: [TEST_IDS.scorePanel, TEST_IDS.scoreStat, TEST_IDS.gradeValue, TEST_IDS.resultSection,
    TEST_IDS.estimatedScore, TEST_IDS.modelAnswer, TEST_IDS.criteriaList],
};
export const GRADING_STEP_MODEL_ANSWER = 'Preparing model answer';
export const RESULT_SECTION_ORDER = ['score', 'priorities', 'model-answer', 'criteria', 'corrections', 'reference', 'next-actions'];
export const RESULT_SECTIONS_REQUIRED = ['score', 'priorities', 'model-answer', 'criteria', 'corrections', 'next-actions'];

export const SELECTORS = {
  editorInput: 'div.ProseMirror#practice-editor',
  mainContent: '#main-content',
  bottomNav: 'nav[aria-label="Mobile navigation"]',
  handle: `[data-testid="${TEST_IDS.shellHandle}"], button[aria-label="Open quick access menu"]`,
  mobileMenuButton: 'button[aria-controls="mobile-menu"]',
  signInEmail: 'input[name="email"]',
  signInPassword: 'input[name="password"]',
  signInSubmit: 'form button[type="submit"]',
};

export const DESKTOP_WIDTHS = [1280, 1366, 1440, 1536];
export const MOBILE_VIEWPORTS = [{ width: 360, height: 780 }, { width: 390, height: 844 }, { width: 430, height: 932 }];
