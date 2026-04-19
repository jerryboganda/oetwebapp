'use client';

import { useEffect } from 'react';
import {
  BookOpen,
  Users,
  FileText,
  CreditCard,
  Shield,
  BarChart3,
  AlertTriangle,
  CheckCircle2,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Accordion } from '@/components/ui/accordion';
import { MotionItem } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { analytics } from '@/lib/analytics';

const PLAYBOOK_SECTIONS = [
  {
    title: 'Content Management',
    icon: FileText,
    accent: 'primary' as const,
    workflows: [
      {
        name: 'Publish New Content',
        steps: [
          'Create content item with metadata (subtest, difficulty, profession)',
          'Add questions/prompts and answer keys',
          'Set status to "Draft" for review',
          'Run quality checks (spelling, rubric alignment)',
          'Move to "Published" — immediately available to learners',
        ],
      },
      {
        name: 'Retire Content',
        steps: [
          'Check usage analytics for active learners',
          'Set status to "Archived" (soft delete)',
          'Content no longer appears in practice pools',
          'Historical attempts and scores are preserved',
        ],
      },
      {
        name: 'Handle Content Complaints',
        steps: [
          'Review the flagged content item',
          'Check recent scores for anomalies',
          'Fix issues and republish, or archive if unfixable',
          'Respond to learner feedback',
        ],
      },
    ],
  },
  {
    title: 'User Management',
    icon: Users,
    accent: 'emerald' as const,
    workflows: [
      {
        name: 'Onboard New Expert',
        steps: [
          'Create expert account with credentials',
          'Assign profession specialisations',
          'Complete calibration reviews (minimum 3)',
          'Monitor initial review quality via AI alignment score',
          'Grant full review access after calibration',
        ],
      },
      {
        name: 'Handle Account Issues',
        steps: [
          'Search user by email or ID',
          'Check subscription status and payment history',
          'Review login activity and last active date',
          'Reset password or unlock account as needed',
          'Document resolution in notes',
        ],
      },
    ],
  },
  {
    title: 'Review Operations',
    icon: Shield,
    accent: 'purple' as const,
    workflows: [
      {
        name: 'Monitor SLA Compliance',
        steps: [
          'Check SLA Health dashboard daily',
          'Identify reviews approaching breach (< 4 hours remaining)',
          'Redistribute overloaded expert queues',
          'Escalate breached reviews with priority flag',
          'Document chronic bottlenecks for staffing decisions',
        ],
      },
      {
        name: 'Handle Expert Disagreements',
        steps: [
          'Review calibration session flagged items',
          'Compare expert scores with AI baseline and consensus',
          'Convene calibration discussion if spread > 2 bands',
          'Update rubric guidance if interpretation varies',
          'Re-assign review if expert was clearly miscalibrated',
        ],
      },
    ],
  },
  {
    title: 'Billing & Credits',
    icon: CreditCard,
    accent: 'amber' as const,
    workflows: [
      {
        name: 'Handle Refund Requests',
        steps: [
          'Verify the failed or unsatisfactory review',
          'Check credit lifecycle policy for eligibility',
          'Issue credit refund to learner wallet',
          'Document reason for audit trail',
          'Flag expert if review quality was the cause',
        ],
      },
      {
        name: 'Plan Changes',
        steps: [
          'Review current subscription details',
          'Check proration rules for mid-cycle changes',
          'Process upgrade/downgrade',
          'Verify new entitlements are active',
          'Confirm billing amount adjustment',
        ],
      },
    ],
  },
  {
    title: 'Analytics & Reporting',
    icon: BarChart3,
    accent: 'blue' as const,
    workflows: [
      {
        name: 'Weekly Review',
        steps: [
          'Check Subscription Health for MRR and churn trends',
          'Review Expert Efficiency for throughput bottlenecks',
          'Scan Content Effectiveness for underperforming items',
          'Review Cohort Analysis for engagement patterns',
          'Export findings to stakeholder report',
        ],
      },
    ],
  },
  {
    title: 'Incident Response',
    icon: AlertTriangle,
    accent: 'rose' as const,
    workflows: [
      {
        name: 'SLA Breach Escalation',
        steps: [
          'Identify affected learners from SLA Health alerts',
          'Send apology notification with updated ETA',
          'Reassign to available expert with shortest queue',
          'Offer credit compensation per policy',
          'Post-mortem: update capacity planning',
        ],
      },
      {
        name: 'System Outage Impact',
        steps: [
          'Assess affected services (API, payments, reviews)',
          'Pause SLA clocks for impacted reviews',
          'Communicate status via system banner',
          'Resume SLA tracking after resolution',
          'Compensate affected users if warranted',
        ],
      },
    ],
  },
];

export default function AdminPlaybookPage() {
  useEffect(() => {
    analytics.track('admin_playbook_viewed');
  }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Admin operational playbook">
      <AdminRouteHero
        eyebrow="Operations"
        icon={BookOpen}
        accent="navy"
        title="Admin Operational Playbook"
        description="Standard workflows and procedures for platform operations across content, users, reviews, billing, analytics, and incidents."
        highlights={[
          { label: 'Sections', value: String(PLAYBOOK_SECTIONS.length) },
          {
            label: 'Workflows',
            value: String(PLAYBOOK_SECTIONS.reduce((sum, s) => sum + s.workflows.length, 0)),
          },
        ]}
      />

      {PLAYBOOK_SECTIONS.map((section) => (
        <AdminRoutePanel
          key={section.title}
          eyebrow={section.title}
          title={section.title}
          description={`${section.workflows.length} workflow${section.workflows.length === 1 ? '' : 's'}`}
        >
          <div className="space-y-3">
            {section.workflows.map((wf) => (
              <MotionItem key={wf.name}>
                <Accordion
                  items={[
                    {
                      id: wf.name,
                      title: (
                        <span className="flex items-center gap-2">
                          <Badge variant="outline">{wf.steps.length} steps</Badge>
                          <span>{wf.name}</span>
                        </span>
                      ),
                      content: (
                        <ol className="space-y-2">
                          {wf.steps.map((step, i) => (
                            <li key={i} className="flex items-start gap-3 text-sm">
                              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-primary/10 text-[11px] font-bold text-primary">
                                {i + 1}
                              </span>
                              <span className="text-navy">{step}</span>
                            </li>
                          ))}
                          <li className="flex items-center gap-2 pt-2 text-xs text-muted">
                            <CheckCircle2 className="h-3.5 w-3.5 text-success" aria-hidden /> Workflow complete
                          </li>
                        </ol>
                      ),
                    },
                  ]}
                />
              </MotionItem>
            ))}
          </div>
        </AdminRoutePanel>
      ))}
    </AdminRouteWorkspace>
  );
}
