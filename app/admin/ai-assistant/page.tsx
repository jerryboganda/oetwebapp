'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { BarChart3, Bot, Cpu, MessageSquare, Settings, Users, Zap } from 'lucide-react';

import {
  AdminOperationsLayout,
  KpiStrip,
} from '@/components/admin/layout/admin-operations-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/admin/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { cn } from '@/lib/utils';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';

interface AiAssistantStats {
  totalThreads: number;
  activeSessions: number;
  totalMessages: number;
  totalTokensUsed: number;
  totalPromptTokens: number;
  totalCompletionTokens: number;
  estimatedCost: number;
}

type PageStatus = 'loading' | 'success' | 'error';

function QuickLink({
  href,
  icon: Icon,
  label,
  description,
}: {
  href: string;
  icon: React.ElementType;
  label: string;
  description: string;
}) {
  return (
    <Link
      href={href}
      className={cn(
        'group flex items-start gap-3 rounded-admin border border-admin-border bg-admin-bg-surface p-4',
        'transition-[background-color,border-color,box-shadow] duration-150',
        '[@media(hover:hover)]:hover:bg-admin-bg-subtle [@media(hover:hover)]:hover:border-admin-border-strong',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--admin-bg-page)]',
      )}
    >
      <span
        aria-hidden="true"
        className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-admin-sm bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]"
      >
        <Icon className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <p className="text-sm font-semibold text-admin-fg-strong">{label}</p>
        <p className="mt-0.5 text-xs text-admin-fg-muted">{description}</p>
      </div>
    </Link>
  );
}

export default function AiAssistantOverviewPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [stats, setStats] = useState<AiAssistantStats | null>(null);

  const loadStats = useCallback(async () => {
    try {
      setStatus('loading');
      const data = await apiClient.get<AiAssistantStats>('/v1/admin/ai-assistant/stats');
      setStats(data);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadStats();
    }
  }, [isAuthenticated, role, loadStats]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminOperationsLayout
        title="AI Assistant"
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'AI Assistant' }]}
        primaryGrid={
          <EmptyState
            illustration={<Bot />}
            title="Admin access required"
            description="Sign in with an admin account to view AI assistant statistics."
          />
        }
      />
    );
  }

  return (
    <AdminOperationsLayout
      title="AI Assistant"
      description="Threads, sessions, token usage, and cost across the assistant surface."
      eyebrow="AI"
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'AI Assistant' }]}
      kpis={
        stats ? (
          <KpiStrip>
            <KpiTile
              label="Total Threads"
              value={stats.totalThreads.toLocaleString()}
              icon={<MessageSquare className="h-4 w-4" />}
              tone="primary"
            />
            <KpiTile
              label="Active Sessions"
              value={stats.activeSessions.toLocaleString()}
              icon={<Zap className="h-4 w-4" />}
              tone="success"
            />
            <KpiTile
              label="Total Messages"
              value={stats.totalMessages.toLocaleString()}
              icon={<Bot className="h-4 w-4" />}
              tone="info"
            />
            <KpiTile
              label="Token Usage"
              value={stats.totalTokensUsed.toLocaleString()}
              icon={<Cpu className="h-4 w-4" />}
              tone="warning"
            />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <AsyncStateWrapper status={status} onRetry={loadStats}>
          {stats && (
            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <CardTitle>Token Breakdown</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                    <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                      <p className="text-xs text-admin-fg-muted">Prompt Tokens</p>
                      <p className="mt-1 text-lg font-bold text-admin-fg-strong tabular-nums">
                        {stats.totalPromptTokens.toLocaleString()}
                      </p>
                    </div>
                    <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                      <p className="text-xs text-admin-fg-muted">Completion Tokens</p>
                      <p className="mt-1 text-lg font-bold text-admin-fg-strong tabular-nums">
                        {stats.totalCompletionTokens.toLocaleString()}
                      </p>
                    </div>
                    <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                      <p className="text-xs text-admin-fg-muted">Estimated Cost</p>
                      <p className="mt-1 text-lg font-bold tabular-nums text-[var(--admin-success)]">
                        ${stats.estimatedCost.toFixed(2)}
                      </p>
                    </div>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle>Quick Links</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    <QuickLink
                      href="/admin/ai-assistant/threads"
                      icon={MessageSquare}
                      label="Manage Threads"
                      description="View and manage all AI assistant conversations"
                    />
                    <QuickLink
                      href="/admin/ai-assistant/config"
                      icon={Settings}
                      label="Configuration"
                      description="Model settings, token limits, and tool access"
                    />
                    <QuickLink
                      href="/admin/ai-assistant/analytics"
                      icon={BarChart3}
                      label="Analytics"
                      description="Usage metrics, response times, and costs"
                    />
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <Users className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
                    System Status
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="success" intensity="tinted">
                      AI Service Online
                    </Badge>
                    <Badge variant="info" intensity="tinted">
                      {stats.activeSessions} active session{stats.activeSessions !== 1 ? 's' : ''}
                    </Badge>
                    <Badge variant="default" intensity="tinted">
                      {stats.totalThreads} total thread{stats.totalThreads !== 1 ? 's' : ''}
                    </Badge>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}
        </AsyncStateWrapper>
      }
    />
  );
}
