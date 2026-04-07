import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { SignupBillingPlan } from '@/lib/types/auth';

function formatPrice(amount: number, currency: string): string {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
  }).format(amount);
}

function formatSubtests(subtests: string[]): string {
  return subtests.length > 0 ? subtests.join(' / ') : 'No subtests listed';
}

interface RegisterPlanPreviewProps {
  billingPlans: SignupBillingPlan[];
}

export function RegisterPlanPreview({ billingPlans }: RegisterPlanPreviewProps) {
  return (
    <div className={styles.summaryCard}>
      <h4>Published Billing Plans</h4>
      <p className={styles.fieldHint}>
        These plans come from the admin-managed catalog and stay ordered by display priority.
      </p>

      {billingPlans.length === 0 ? (
        <p className={styles.fieldHint}>No published billing plans are available yet.</p>
      ) : (
        <div className={styles.summaryList}>
          {billingPlans.map((plan) => {
            const entitlements = plan.entitlements ?? {};
            const invoiceDownloads = entitlements['invoiceDownloadsAvailable'] === true;
            const productiveReviews = entitlements['productiveSkillReviewsEnabled'] === true;

            return (
              <div key={plan.id} className={styles.summaryItem}>
                <span className={styles.summaryIcon}>{plan.displayOrder}</span>
                <div>
                  <p>
                    <strong>{plan.label}</strong> {plan.isVisible ? '(visible)' : '(hidden)'}
                  </p>
                  <p>
                    {formatPrice(plan.priceAmount, plan.currency)} / {plan.interval}
                  </p>
                  <p>{plan.reviewCredits} review credits</p>
                  <p>{formatSubtests(plan.includedSubtests)}</p>
                  <p>
                    {plan.isRenewable ? 'Renews automatically' : 'One-time term'} /{' '}
                    {plan.trialDays > 0 ? `${plan.trialDays} day trial` : 'No trial'}
                  </p>
                  <p>
                    {productiveReviews ? 'Productive skill reviews included' : 'Productive reviews limited'} /{' '}
                    {invoiceDownloads ? 'Invoice downloads enabled' : 'Invoice downloads unavailable'}
                  </p>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
