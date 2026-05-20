'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { Bot, MessageSquare, Settings, BarChart3, Cpu, Users, Zap } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { EmptyState } from '@/components/ui/empty-error';
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

function StatCard({
  label,
  value,
  icon: Icon,
  accent = 'violet',
}: {
  label: string;
  value: string | number;
  icon: React.ElementType;
  accent?: 'violet' | 'blue' | 'emerald' | 'amber';
}) {
  const accentMap = {
    violet: 'bg-violet-500/15 text-violet-400',
    blue: 'bg-blue-500/15 text-blue-400',
    emerald: 'bg-emerald-500/15 text-emerald-400',
    amber: 'bg-amber-500/15 text-amber-400',
  };

  return (
    <div className="rounded-xl border border-admin-border bg-admin-surface p-4">
      <div className="flex items-center gap-3">
        <div className={`flex h-10 w-10 items-center justify-center rounded-lg ${accentMap[accent]}`}>
          <Icon className="h-5 w-5" />
        </div>
        <div className="min-w-0">
          <p className="text-xs font-bold uppercase tracking-wide text-admin-text-muted">{label}</p>
          <p className="mt-0.5 text-xl font-bold text-admin-text">{typeof value === 'number' ? value.toLocaleString() : value}</p>
        </div>
      </div>
    </div>
  );
}

function QuickLink({ href, icon: Icon, label, description }: { href: string; icon: React.ElementType; label: string; description: string }) {
  return (
    <Link
      href={href}
      className="group flex items-start gap-3 rounded-xl border border-admin-border bg-admin-surface p-4 transition-colors hover:bg-admin-surface-raised"
    >
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-violet-500/15 text-violet-400 transition-colors group-hover:bg-violet-500/25">
        <Icon className="h-4.5 w-4.5" />
      </div>
      <div className="min-w-0">
        <p className="text-sm font-semibold text-admin-text">{label}</p>
        <p className="mt-0.5 text-xs text-admin-text-muted">{description}</p>
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
      // Data fetch on mount — setState inside async callback is fine
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadStats();
    }
  }, [isAuthenticated, role, loadStats]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<Bot className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AsyncStateWrapper status={status} onRetry={loadStats}>
        {stats && (
          <>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <StatCard label="Total Threads" value={stats.totalThreads} icon={MessageSquare} accent="violet" />
              <StatCard label="Active Sessions" value={stats.activeSessions} icon={Zap} accent="emerald" />
              <StatCard label="Total Messages" value={stats.totalMessages} icon={Bot} accent="blue" />
              <StatCard label="Token Usage" value={stats.totalTokensUsed.toLocaleString()} icon={Cpu} accent="amber" />
            </div>

            <AdminRoutePanel>
              <div className="p-4">
                <h3 className="text-sm font-bold text-admin-text">Token Breakdown</h3>
                <div className="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">Prompt Tokens</p>
                    <p className="mt-1 text-lg font-bold text-admin-text">{stats.totalPromptTokens.toLocaleString()}</p>
                  </div>
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">Completion Tokens</p>
                    <p className="mt-1 text-lg font-bold text-admin-text">{stats.totalCompletionTokens.toLocaleString()}</p>
                  </div>
                  <div className="rounded-lg bg-admin-surface-raised p-3">
                    <p className="text-xs text-admin-text-muted">Estimated Cost</p>
                    <p className="mt-1 text-lg font-bold text-emerald-400">${stats.estimatedCost.toFixed(2)}</p>
                  </div>
                </div>
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel>
              <div className="p-4">
                <h3 className="text-sm font-bold text-admin-text">Quick Links</h3>
                <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
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
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel>
              <div className="p-4">
                <div className="flex items-center gap-2">
                  <Users className="h-4 w-4 text-admin-text-muted" />
                  <h3 className="text-sm font-bold text-admin-text">System Status</h3>
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Badge variant="success">AI Service Online</Badge>
                  <Badge variant="info">{stats.activeSessions} active session{stats.activeSessions !== 1 ? 's' : ''}</Badge>
                  <Badge variant="default">{stats.totalThreads} total thread{stats.totalThreads !== 1 ? 's' : ''}</Badge>
                </div>
              </div>
            </AdminRoutePanel>
          </>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
