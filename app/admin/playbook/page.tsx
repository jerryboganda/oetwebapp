'use client';

import { useEffect } from 'react';
import {
  AlertTriangle,
  BarChart3,
  BookOpen,
  CheckCircle2,
  CreditCard,
  FileText,
  Shield,
  Users,
} from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { analytics } from '@/lib/analytics';

const PLAYBOOK_SECTIONS = [
  {
    title: 'Content Management',
    icon: FileText,
    workflows: [
      { name: 'Publish New Content', steps: ['Create content item with metadata (subtest, difficulty, profession)', 'Add questions/prompts and answer keys', 'Set status to "Draft" for review', 'Run quality checks (spelling, rubric alignment)', 'Move to "Published" — immediately available to learners'] },
      { name: 'Retire Content', steps: ['Check usage analytics for active learners', 'Set status to "Archived" (soft delete)', 'Content no longer appears in practice pools', 'Historical attempts and scores are preserved'] },
      { name: 'Handle Content Complaints', steps: ['Review the flagged content item', 'Check recent scores for anomalies', 'Fix issues and republish, or archive if unfixable', 'Respond to learner feedback'] },
    ],
  },
  {
    title: 'User Management',
    icon: Users,
    workflows: [
      { name: 'Onboard New Tutor', steps: ['Create tutor account with credentials', 'Assign profession specialisations', 'Complete calibration reviews (minimum 3)', 'Monitor initial review quality via AI alignment score', 'Grant full review access after calibration'] },
      { name: 'Handle Account Issues', steps: ['Search user by email or ID', 'Check subscription status and payment history', 'Review login activity and last active date', 'Reset password or unlock account as needed', 'Document resolution in notes'] },
    ],
  },
  {
    title: 'Review Operations',
    icon: Shield,
    workflows: [
      { name: 'Monitor SLA Compliance', steps: ['Check SLA Health dashboard daily', 'Identify reviews approaching breach (< 4 hours remaining)', 'Redistribute overloaded tutor queues', 'Escalate breached reviews with priority flag', 'Document chronic bottlenecks for staffing decisions'] },
      { name: 'Handle Tutor Disagreements', steps: ['Review calibration session flagged items', 'Compare tutor scores with AI baseline and consensus', 'Convene calibration discussion if spread > 2 bands', 'Update rubric guidance if interpretation varies', 'Re-assign review if tutor was clearly miscalibrated'] },
    ],
  },
  {
    title: 'Billing & Credits',
    icon: CreditCard,
    workflows: [
      { name: 'Handle Refund Requests', steps: ['Verify the failed or unsatisfactory review', 'Check credit lifecycle policy for eligibility', 'Issue credit refund to learner wallet', 'Document reason for audit trail', 'Flag tutor if review quality was the cause'] },
      { name: 'Plan Changes', steps: ['Review current subscription details', 'Check proration rules for mid-cycle changes', 'Process upgrade/downgrade', 'Verify new entitlements are active', 'Confirm billing amount adjustment'] },
    ],
  },
  {
    title: 'Analytics & Reporting',
    icon: BarChart3,
    workflows: [
      { name: 'Weekly Review', steps: ['Check Subscription Health for MRR and churn trends', 'Review Tutor Efficiency for throughput bottlenecks', 'Scan Content Effectiveness for underperforming items', 'Review Cohort Analysis for engagement patterns', 'Export findings to stakeholder report'] },
    ],
  },
  {
    title: 'Incident Response',
    icon: AlertTriangle,
    workflows: [
      { name: 'SLA Breach Escalation', steps: ['Identify affected learners from SLA Health alerts', 'Send apology notification with updated ETA', 'Reassign to available tutor with shortest queue', 'Offer credit compensation per policy', 'Post-mortem: update capacity planning'] },
      { name: 'System Outage Impact', steps: ['Assess affected services (API, payments, reviews)', 'Pause SLA clocks for impacted reviews', 'Communicate status via system banner', 'Resume SLA tracking after resolution', 'Compensate affected users if warranted'] },
    ],
  },
];

export default function AdminPlaybookPage() {
  useEffect(() => { analytics.track('admin_playbook_viewed'); }, []);

  return (
    <AdminSettingsLayout
      title="Admin operational playbook"
      description="Standard workflows and procedures for platform operations."
      eyebrow="Operations"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Playbook' },
      ]}
    >
      {PLAYBOOK_SECTIONS.map((section) => {
        const Icon = section.icon;
        return (
          <SettingsSection
            key={section.title}
            title={
              <span className="inline-flex items-center gap-2">
                <Icon className="h-4 w-4 text-[var(--admin-primary)]" aria-hidden="true" />
                {section.title}
              </span>
            }
            description={`${section.workflows.length} workflow${section.workflows.length === 1 ? '' : 's'}`}
          >
            <div className="space-y-5">
              {section.workflows.map((wf) => (
                <div
                  key={wf.name}
                  className="rounded-admin border border-admin-border bg-admin-bg-surface p-4"
                >
                  <h4 className="mb-3 flex items-center gap-2 font-semibold text-admin-fg-strong">
                    <Badge variant="default" intensity="tinted">
                      {wf.steps.length} steps
                    </Badge>
                    <span>{wf.name}</span>
                  </h4>
                  <ol className="space-y-2">
                    {wf.steps.map((step, i) => (
                      <li key={i} className="flex items-start gap-3 text-sm text-admin-fg-default">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-admin-fg-muted" aria-hidden="true" />
                        <span>{step}</span>
                      </li>
                    ))}
                  </ol>
                </div>
              ))}
            </div>
          </SettingsSection>
        );
      })}
    </AdminSettingsLayout>
  );
}
