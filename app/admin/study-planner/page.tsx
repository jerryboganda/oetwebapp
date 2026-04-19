'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { Calendar, FileText, Scale, AlertTriangle, BarChart3, Sparkles, ClipboardList } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getStudyPlannerInsights, type StudyPlannerInsights } from '@/lib/study-planner-admin-api';

interface HubCard {
  href: string;
  icon: React.ElementType;
  title: string;
  description: string;
  badge?: string;
}

const CARDS: HubCard[] = [
  {
    href: '/admin/study-planner/tasks',
    icon: ClipboardList,
    title: 'Task Templates',
    description: 'Author the library of reusable study tasks (title, rationale, duration, deep-link to content).',
    badge: 'Core',
  },
  {
    href: '/admin/study-planner/templates',
    icon: FileText,
    title: 'Plan Templates',
    description: 'Group task templates into ordered week-by-week plans that learners can be assigned to.',
    badge: 'Core',
  },
  {
    href: '/admin/study-planner/rules',
    icon: Scale,
    title: 'Assignment Rules',
    description: 'Match learners to plan templates by profession, target country, weeks-to-exam, and weak skills.',
  },
  {
    href: '/admin/study-planner/drift-policy',
    icon: AlertTriangle,
    title: 'Drift Policy',
    description: 'Configure the thresholds and copy used to detect (and optionally auto-regenerate) drifting plans.',
  },
  {
    href: '/admin/study-planner/insights',
    icon: BarChart3,
    title: 'Insights',
    description: 'Fleet-wide completion rate, overdue items, and recent regeneration activity.',
  },
];

export default function StudyPlannerAdminHub() {
  const { isAuthenticated, role } = useAdminAuth();
  const [insights, setInsights] = useState<StudyPlannerInsights | null>(null);

  const load = useCallback(async () => {
    try {
      const data = await getStudyPlannerInsights();
      setInsights(data);
    } catch {
      // optional
    }
  }, []);

  useEffect(() => {
    queueMicrotask(() => { void load(); });
  }, [load]);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<Calendar className="w-6 h-6" />}
        title="Study Planner"
        description="End-to-end control of the learner Study Planner: author tasks, compose plans, define rules, tune drift thresholds, and monitor the fleet."
      />

      {insights && (
        <AdminRoutePanel eyebrow="Fleet Snapshot" title="Current activity" dense>
          <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
            <Stat label="Plans" value={insights.totalPlans} />
            <Stat label="Items" value={insights.totalItems} />
            <Stat label="Completion" value={`${insights.completionRate}%`} />
            <Stat label="Overdue" value={insights.overdueItems} tone={insights.overdueItems > 0 ? 'warning' : 'default'} />
            <Stat label="Regens (7d)" value={insights.regenLast7d} />
          </div>
        </AdminRoutePanel>
      )}

      <AdminRoutePanel eyebrow="Sections" title="Manage planner content">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {CARDS.map((card) => {
            const Icon = card.icon;
            return (
              <Link key={card.href} href={card.href} className="group">
                <div className="h-full p-5 rounded-xl border border-gray-200 bg-surface hover:border-primary hover:shadow-md transition-all">
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3">
                      <div className="p-2.5 rounded-lg bg-primary/10 text-primary">
                        <Icon className="w-5 h-5" />
                      </div>
                      <div>
                        <h3 className="font-bold text-navy group-hover:text-primary">{card.title}</h3>
                        {card.badge && <Badge variant="info" className="mt-1">{card.badge}</Badge>}
                      </div>
                    </div>
                    <Sparkles className="w-4 h-4 text-muted opacity-0 group-hover:opacity-100 transition-opacity" />
                  </div>
                  <p className="text-sm text-muted mt-3 leading-relaxed">{card.description}</p>
                </div>
              </Link>
            );
          })}
        </div>
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}

function Stat({ label, value, tone }: { label: string; value: number | string; tone?: 'default' | 'warning' }) {
  return (
    <div className={`p-3 rounded-lg border ${tone === 'warning' ? 'border-amber-200 bg-amber-50' : 'border-gray-200 bg-surface'}`}>
      <p className="text-xs font-semibold uppercase tracking-wider text-muted">{label}</p>
      <p className={`text-2xl font-bold mt-1 ${tone === 'warning' ? 'text-amber-700' : 'text-navy'}`}>{value}</p>
    </div>
  );
}
