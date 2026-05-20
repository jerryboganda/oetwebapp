'use client';

import type { JSX } from 'react';
import { AdminRouteSectionHeader, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { AdminPermission } from '@/lib/admin-permissions';

interface MatrixRow {
  permission: string;
  label: string;
  surface: string;
  description: string;
  required: boolean;
}

const AI_ASSISTANT_ROWS: MatrixRow[] = [
  {
    permission: 'ai_assistant:use',
    label: 'Use AI Assistant',
    surface: '/admin/ai-assistant (chat)',
    description: 'Open the chat surface, start threads, send messages.',
    required: true,
  },
  {
    permission: 'ai_assistant:manage',
    label: 'Manage AI Assistant',
    surface: '/admin/ai-assistant/{audit,usage,threads,providers,indexing,test-console}',
    description: 'Toggle kill-switch, view audit + usage logs, see all users\' threads, view providers, trigger reindex.',
    required: true,
  },
  {
    permission: 'ai_assistant:unrestricted',
    label: 'Unrestricted Tools',
    surface: 'Tool invocations (Phase 3+)',
    description: 'Skip per-tool admin approval for write_file / run_command / git when invoked by the executor.',
    required: false,
  },
];

export default function AiAssistantRoleMatrixPage(): JSX.Element {
  const columns: Column<MatrixRow>[] = [
    { key: 'label', header: 'Permission', render: (r) => <span className="font-medium">{r.label}</span> },
    {
      key: 'permission',
      header: 'Key',
      render: (r) => <code className="text-xs font-mono">{r.permission}</code>,
    },
    {
      key: 'required',
      header: 'Required',
      render: (r) => (r.required ? <Badge variant="danger">Required</Badge> : <Badge variant="muted">Optional</Badge>),
    },
    { key: 'surface', header: 'Surface', render: (r) => <span className="text-xs text-admin-text-muted">{r.surface}</span> },
    { key: 'description', header: 'Description', render: (r) => <span className="text-xs">{r.description}</span> },
  ];

  const otherAdminPerms = Object.keys(AdminPermission).length;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — Role Matrix"
        description={`Maps the 3 AI Assistant permissions to admin surfaces. The wider admin RBAC layer carries ${otherAdminPerms} total permissions defined in lib/admin-permissions.ts.`}
      />
      <AdminRoutePanel>
        <DataTable<MatrixRow> data={AI_ASSISTANT_ROWS} columns={columns} keyExtractor={(r) => r.permission} />
      </AdminRoutePanel>
      <AdminRoutePanel
        title="Server-side enforcement"
        description={'Every admin endpoint under /v1/admin/ai-assistant/** carries [Authorize(Policy="AdminAiAssistantManage")] AND PerUserWrite rate-limit. Chat endpoints under /v1/ai-assistant/** require AiAssistantUse.'}
      >
        <p className="text-xs text-admin-text-muted">
          Source of truth: <code>backend/src/OetLearner.Api/Endpoints/AiAssistantAdminEndpoints.cs</code> and <code>backend/src/OetLearner.Api/Endpoints/AiAssistantChatEndpoints.cs</code>.
        </p>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
