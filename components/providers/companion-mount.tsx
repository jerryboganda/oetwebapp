'use client';

import { useAuth } from '@/contexts/auth-context';
import { useFeatureFlagMap } from '@/hooks/use-feature-flag-map';
import { AiAssistantWidget } from '@/components/domain/ai-assistant';

/**
 * Mount point for the AI Learning Companion floating launcher.
 *
 * Two gates, both fail-closed:
 *
 *  1. **Authentication** — the flag endpoint is `LearnerOnly`, so an anonymous
 *     visitor never even asks. Nothing renders.
 *  2. **`ai_learning_companion` feature flag** — server-owned, backed by the
 *     `FeatureFlags` table, so the surface can be enabled or killed from
 *     `/admin/flags` without a deploy. `useFeatureFlagMap` retries twice and
 *     then resolves `false`, so a flaky flag read hides the companion rather
 *     than exposing it.
 *
 * The flag ships **disabled** (`flg-026`). Mounting this component does not by
 * itself make the companion visible to anyone.
 *
 * See docs/ai-learning-companion/.
 */
const COMPANION_FLAG = 'ai_learning_companion';

export function CompanionMount() {
  const { isAuthenticated, role } = useAuth();
  const flags = useFeatureFlagMap([COMPANION_FLAG], isAuthenticated);

  if (!isAuthenticated) return null;
  if (!flags[COMPANION_FLAG]) return null;

  // The widget applies its own role check (`canAccessAiAssistant`) on top.
  return <AiAssistantWidget role={role} />;
}
