'use client';

import { useAuth } from '@/contexts/auth-context';
import { useFeatureFlagMap } from '@/hooks/use-feature-flag-map';
import { AiAssistantWidget } from '@/components/domain/ai-assistant';

/**
 * Mount point for the AI assistant floating launcher.
 *
 * Two surfaces share one widget:
 *
 * - **Learners** — the AI Learning Companion, gated on the server-owned
 *   `ai_learning_companion` flag (`LearnerOnly` endpoint, fail-closed,
 *   ships disabled as `flg-026`). See docs/ai-learning-companion/.
 * - **Admins** — the developer chatbot (`ai_assistant.admin`, full codebase
 *   toolset via AiFeatureToolGrants, SafetyGuard admin-gated mutations).
 *   Mounted unconditionally: staff need it even with the learner flag off,
 *   and its own permission check (`canAccessAiAssistant`) plus the
 *   role-derived server feature code keep learners out.
 *
 * Experts and unauthenticated visitors render nothing here (the widget's own
 * role check is authoritative; this mount only avoids the wasted flag fetch
 * for non-learners).
 */
const COMPANION_FLAG = 'ai_learning_companion';

export function CompanionMount() {
  const { isAuthenticated, role } = useAuth();
  const isAdmin = isAuthenticated && role === 'admin';
  const isLearner = isAuthenticated && role === 'learner';
  const flags = useFeatureFlagMap([COMPANION_FLAG], isLearner);

  // Admin chatbot: bottom-right, always available to staff — never gated on
  // the learner companion flag.
  if (isAdmin) return <AiAssistantWidget role={role} />;

  if (!isLearner) return null;
  if (!flags[COMPANION_FLAG]) return null;

  // The widget applies its own role check (`canAccessAiAssistant`) on top.
  return <AiAssistantWidget role={role} />;
}
