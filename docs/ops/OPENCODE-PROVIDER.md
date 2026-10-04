# OpenCode Provider — Owner Runbook

**Effective 2026-10-04.** The `opencode` provider row ships **inactive and keyless**. These are the
owner steps to activate it.

## 1. Rotate the key

The old key was pasted into chat and is compromised. Rotate it in your OpenCode dashboard, then
paste the **new** key only into `/admin/ai-providers` (stored Data-Protection-encrypted).

## 2. Configure the provider row

1. Go to `/admin/ai-providers`.
2. Edit the seeded `opencode` row.
3. Apply the **Zen** preset (`opencode-zen`) → paste the new key → click **Test**.
4. If Test reports `401 AuthError` / `CreditsError`, apply the **Go** preset (`opencode-go`) and
   Test again. That is how Zen vs Go is distinguished.

## 3. Confirm prerequisites

- Flags `ai_learning_companion` (flg-026) and `companion_actions` (flg-028) must exist and be ON
  in production.
- The `AiCompanion` module must be on the learner's packages.
- Token-quota plan caps: raise companion-plan caps if learners hit the free-tier daily limit.

## 4. Activate

After the ToS decision (written OK from OpenCode or accepted risk), tick **Active** on the row.

## 5. Verify

1. In a learner thread, open the model picker and select an OpenCode model (`glm-5.3-flash`).
2. Ask for a study plan. The companion calls `companion_preview_study_plan`, shows a preview,
   and asks for agreement.
3. Agree in the next message. The companion calls `companion_create_study_plan`.
4. Confirm the plan appears at `/study-plan`.
5. Check `AiUsageRecord` rows for the calls.

## Error classes

| HTTP | Error type | Classification |
|------|-----------|----------------|
| 401  | `AuthError` | auth |
| 401  | `CreditsError` / `MonthlyLimitError` | quota |
| 402  | "Insufficient account funds" | quota |
| 429  | `RateLimitError` | rate |
| 429  | `GoUsageLimitError` | quota |
| 400  | `ModelError` | invalid |
| 503  | (any) | provider error |

## Rollback

Deactivate the `opencode` row in `/admin/ai-providers`. Learners see the busy message; the default
route stays Claude and is unaffected.
