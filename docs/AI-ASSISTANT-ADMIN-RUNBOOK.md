# Admin AI Assistant - Current Runbook

> Audience: OET Prep platform owner and `system_admin`s. This runbook describes the currently shipped local-code surface, not the future devbox/pgvector/tooling design.

## 1. Current Status

The Admin AI Assistant is an admin-only chatbot surface for the OET Prep Platform. In the current Phase 1 implementation it can:

- show the widget/sidebar on admin pages only;
- create, list, open, and archive admin-owned chat threads;
- send chatbot turns through the canonical `IAiGatewayService` using `AiFeatureCodes.AdminAiChatbot`;
- enforce the grounded chatbot prompt with `RuleKind.Chatbot` and `AiTaskMode.AssistAdminCommand`;
- stream response frames over SignalR at `/v1/ai-assistant/hub`;
- write canonical `AiUsageRecord` rows for successful and refused turns;
- show read-only admin pages for settings, usage, providers, threads, audit, and indexing status.

The feature remains kill-switch gated. The default safe posture is disabled unless the environment/runtime settings explicitly enable it.

## 2. What Is Not Live

These capabilities are intentionally not part of the current shipped surface:

- no repo file reading or semantic codebase search from chat;
- no pgvector index, embedding jobs, or working reindex endpoint;
- no `oet-devbox` sidecar, shell commands, git operations, service restart, or deploy operations;
- no write tools, patch application, approval nonce flow, or file editing from chat;
- no provider secret entry in `/admin/ai-assistant/providers`;
- no per-thread provider/model override. The chatbot uses the canonical gateway feature route for `admin.ai_chatbot`.

Future work for those areas stays approval-gated and must follow [AI-ASSISTANT-THREAT-ACCEPTANCE.md](AI-ASSISTANT-THREAT-ACCEPTANCE.md) before production enablement.

## 3. Access Control

- Admin role is required for REST endpoints and the SignalR hub.
- `AdminAiAssistantUse` is required for chat thread and hub access.
- `AdminAiAssistantManage` is required for management pages/endpoints.
- Non-admin users must not receive the widget bundle and must not receive useful endpoint or hub responses.
- Hub cancel requests and REST cancel requests are scoped to messages owned by the authenticated admin.

## 4. Runtime Paths

| Surface | Path |
| --- | --- |
| Chat REST | `/v1/ai-assistant/*` |
| Admin management REST | `/v1/admin/ai-assistant/*` |
| SignalR hub | `/v1/ai-assistant/hub` |
| Browser proxy form | `/api/backend/v1/ai-assistant/hub` |

When the frontend API base URL is the same-origin `/api/backend` proxy, the browser SignalR client must use long polling. The Next.js proxy handles HTTP requests, not WebSocket upgrades.

## 5. First-Time Enablement

1. Verify the canonical AI provider registry and feature route for `admin.ai_chatbot` in the main AI provider/admin surfaces.
2. Keep provider secrets in the approved runtime settings/provider system. Do not paste API keys into `/admin/ai-assistant/providers`; that page is read-only for this module.
3. Verify `AiAssistant:GlobalEnabled` is set only in the intended environment.
4. Open `/admin/ai-assistant` as a `system_admin` and confirm the settings card reflects the intended enabled/disabled state.
5. Open an admin page, open the assistant widget, start a short conversation, and confirm a canonical usage row appears under `/admin/ai-assistant/usage`.

## 6. Daily Operation

- Use the widget for admin Q&A within the grounded chatbot guardrails.
- Use Cancel to stop the currently streaming turn.
- Archive old threads from the thread list or widget delete action.
- Use `/admin/ai-assistant/providers` only to inspect the gateway route/provider currently selected for the feature.
- Use `/admin/ai-assistant/indexing` only as a status page; `reindex` is expected to remain unavailable until the pgvector phase is approved and implemented.

## 7. Emergency Disable

If the assistant must be disabled:

1. Set `AiAssistant:GlobalEnabled=false` through the approved runtime/environment path for the environment.
2. Use the admin kill-switch endpoint or UI where available.
3. Confirm new hub connections are refused and the widget shows the disabled state.
4. Review `AiUsageRecord` and `AiAuditEvent` rows for the suspect window.

Do not run production VPS, Docker, database, or Nginx Proxy Manager commands from this runbook without explicit owner approval and the normal production safety checks.

## 8. Troubleshooting

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Widget not visible | User is not an admin or client permissions do not include the assistant | Check current user role/permissions and admin layout gating. |
| Cannot connect to hub | Kill switch disabled, auth token missing, or proxy transport mismatch | Check `AiAssistant:GlobalEnabled`, auth, and that relative proxy connections use long polling. |
| Frames appear in the wrong thread | Client/server frame contract drift | Verify streamed frames include `threadId` and the client filters by active thread. |
| Provider unavailable | Canonical gateway route/provider is disabled or misconfigured | Inspect the main AI provider route for `admin.ai_chatbot`; do not edit secrets in the assistant provider page. |
| Indexing shows `not_built` | Expected in current Phase 1 | Leave disabled until the pgvector/indexing phase is approved. |
| Reindex/provider mutation returns `501` | Expected placeholder | Treat as future work, not an operational incident. |

## 9. Related Docs

- [AI-ASSISTANT-PROGRESS.md](AI-ASSISTANT-PROGRESS.md) - implementation status and completed/deferred items
- [AI-ASSISTANT-PLAN.md](AI-ASSISTANT-PLAN.md) - target architecture and historical design notes
- [AI-ASSISTANT-THREAT-ACCEPTANCE.md](AI-ASSISTANT-THREAT-ACCEPTANCE.md) - security gates for future phases
- [AI-ASSISTANT-THREAT-MODEL.md](AI-ASSISTANT-THREAT-MODEL.md) - adversarial review
- [AI-ASSISTANT-DEVOPS.md](AI-ASSISTANT-DEVOPS.md) - future devbox/pgvector deployment design
- [AI-ASSISTANT-ROLLOUT-PLAN.md](AI-ASSISTANT-ROLLOUT-PLAN.md) - phase plan
