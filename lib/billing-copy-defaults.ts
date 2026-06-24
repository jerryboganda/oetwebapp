// Single source of truth for editable learner billing-page copy.
//
// The backend stores ONLY admin overrides (lib → DB). The learner page and the
// admin "Page Copy" editor both fall back to these defaults, so the DB can be
// empty and nothing renders blank. Add a new editable string by adding one row
// here and referencing `copy('<key>')` on the page.

export interface BillingCopyField {
  /** Stable dotted id, e.g. "billing.plans.title". Must start with "billing.". */
  key: string;
  /** Grouping shown in the admin editor. */
  section: BillingCopySection;
  /** Short human label for the admin editor row. */
  label: string;
  /** Default text rendered when no override exists. */
  default: string;
  /** Render as a multi-line textarea in the editor (long copy). */
  multiline?: boolean;
}

export type BillingCopySection =
  | 'Page & hero'
  | 'Tabs'
  | 'Overview'
  | 'Plans'
  | 'Wallet & credits'
  | 'Add-ons'
  | 'Transactions'
  | 'AI packages'
  | 'Invoices';

export const BILLING_COPY_FIELDS: BillingCopyField[] = [
  // ── Page & hero ──────────────────────────────────────────────
  { key: 'billing.page.title', section: 'Page & hero', label: 'Page title', default: 'Billing & subscriptions' },
  { key: 'billing.page.subtitle', section: 'Page & hero', label: 'Page subtitle', default: 'Manage your plan, credits, invoices, and entitlements in one place.', multiline: true },
  { key: 'billing.hero.eyebrow', section: 'Page & hero', label: 'Hero eyebrow', default: 'Billing' },
  { key: 'billing.hero.title', section: 'Page & hero', label: 'Hero title', default: 'Your billing center' },
  { key: 'billing.hero.description', section: 'Page & hero', label: 'Hero description', default: 'Review your plan, top up review credits, and download invoices. Everything stays validated server-side before checkout opens.', multiline: true },
  { key: 'billing.hero.highlight.currentPlan', section: 'Page & hero', label: 'Highlight: current plan', default: 'Current plan' },
  { key: 'billing.hero.highlight.creditBalance', section: 'Page & hero', label: 'Highlight: credit balance', default: 'Credit balance' },
  { key: 'billing.hero.highlight.activeAddons', section: 'Page & hero', label: 'Highlight: active add-ons', default: 'Active add-ons' },
  { key: 'billing.hero.highlight.nextRenewal', section: 'Page & hero', label: 'Highlight: next renewal', default: 'Next renewal' },
  { key: 'billing.quote.validatedLabel', section: 'Page & hero', label: 'Validated quote label', default: 'Validated quote' },
  { key: 'billing.quote.dismiss', section: 'Page & hero', label: 'Quote dismiss button', default: 'Dismiss' },

  // ── Tabs ─────────────────────────────────────────────────────
  { key: 'billing.tab.overview', section: 'Tabs', label: 'Overview tab', default: 'Overview' },
  { key: 'billing.tab.plans', section: 'Tabs', label: 'Plans tab', default: 'Plans' },
  { key: 'billing.tab.credits', section: 'Tabs', label: 'Credits tab', default: 'Credits & Add-ons' },
  { key: 'billing.tab.aiCredits', section: 'Tabs', label: 'AI Credits tab', default: 'AI Credits' },
  { key: 'billing.tab.invoices', section: 'Tabs', label: 'Invoices tab', default: 'Invoices' },

  // ── Overview ─────────────────────────────────────────────────
  { key: 'billing.overview.currentSubscription', section: 'Overview', label: 'Current subscription label', default: 'Current subscription' },
  { key: 'billing.overview.renews', section: 'Overview', label: 'Renews label', default: 'Renews' },
  { key: 'billing.overview.tutorReviews', section: 'Overview', label: 'Tutor reviews label', default: 'Tutor reviews' },
  { key: 'billing.overview.tutorReviewsNotIncluded', section: 'Overview', label: 'Tutor reviews — none', default: 'Not included' },
  { key: 'billing.overview.invoiceAccess', section: 'Overview', label: 'Invoice access label', default: 'Invoice access' },
  { key: 'billing.overview.invoiceAccessAvailable', section: 'Overview', label: 'Invoice access — available', default: 'Downloads available' },
  { key: 'billing.overview.invoiceAccessUnavailable', section: 'Overview', label: 'Invoice access — unavailable', default: 'Unavailable' },
  { key: 'billing.overview.changePlan', section: 'Overview', label: 'Change plan button', default: 'Change plan' },
  { key: 'billing.overview.topUpCredits', section: 'Overview', label: 'Top up credits button', default: 'Top up credits' },
  { key: 'billing.overview.viewInvoices', section: 'Overview', label: 'View invoices button', default: 'View invoices' },
  { key: 'billing.overview.pause', section: 'Overview', label: 'Pause button', default: 'Pause subscription' },
  { key: 'billing.overview.pausing', section: 'Overview', label: 'Pausing state', default: 'Pausing…' },
  { key: 'billing.overview.resume', section: 'Overview', label: 'Resume button', default: 'Resume subscription' },
  { key: 'billing.overview.resuming', section: 'Overview', label: 'Resuming state', default: 'Resuming…' },
  { key: 'billing.overview.creditWallet', section: 'Overview', label: 'Credit wallet label', default: 'Credit wallet' },
  { key: 'billing.overview.creditsAvailable', section: 'Overview', label: 'Credits-available caption', default: 'review credits available' },
  { key: 'billing.overview.creditsHelp', section: 'Overview', label: 'Credit wallet help text', default: 'Credits unlock tutor review for Writing and Speaking. Reading and Listening stay AI-evaluated.', multiline: true },
  { key: 'billing.overview.manageCredits', section: 'Overview', label: 'Manage credits button', default: 'Manage credits' },
  { key: 'billing.overview.activity', section: 'Overview', label: 'Activity eyebrow', default: 'Activity' },
  { key: 'billing.overview.recentInvoices', section: 'Overview', label: 'Recent invoices heading', default: 'Recent invoices' },
  { key: 'billing.overview.viewAll', section: 'Overview', label: 'View all link', default: 'View all →' },
  { key: 'billing.overview.noInvoices', section: 'Overview', label: 'No invoices text', default: 'No invoices yet. They will appear after your first paid checkout.', multiline: true },
  { key: 'billing.overview.recentCreditActivity', section: 'Overview', label: 'Recent credit activity heading', default: 'Recent credit activity' },
  { key: 'billing.overview.noCreditActivity', section: 'Overview', label: 'No credit activity text', default: 'No credit activity yet.' },

  // ── Plans ────────────────────────────────────────────────────
  { key: 'billing.plans.eyebrow', section: 'Plans', label: 'Plans eyebrow', default: 'Plans' },
  { key: 'billing.plans.title', section: 'Plans', label: 'Plans title', default: 'Compare plans and preview a change' },
  { key: 'billing.plans.description', section: 'Plans', label: 'Plans description', default: 'Plans are managed by the admin team. Upgrades and downgrades show a server-validated proration before checkout.', multiline: true },
  { key: 'billing.plans.empty', section: 'Plans', label: 'No plans text', default: 'No published billing plans are available yet.' },
  { key: 'billing.plans.current', section: 'Plans', label: 'Current badge', default: 'Current' },
  { key: 'billing.plans.reviewCreditsIncluded', section: 'Plans', label: 'Review credits caption', default: 'review credits included' },
  { key: 'billing.plans.tutorReviewsFor', section: 'Plans', label: 'Tutor-reviews-for prefix', default: 'Tutor reviews for' },
  { key: 'billing.plans.noSubtests', section: 'Plans', label: 'No subtests fallback', default: 'no subtests' },
  { key: 'billing.plans.trialSuffix', section: 'Plans', label: 'Trial suffix (after "{n}-day")', default: '-day trial' },
  { key: 'billing.plans.autoRenewing', section: 'Plans', label: 'Auto-renewing bullet', default: 'Auto-renewing' },
  { key: 'billing.plans.invoiceDownloads', section: 'Plans', label: 'Invoice downloads bullet', default: 'Invoice downloads' },
  { key: 'billing.plans.previewPrefix', section: 'Plans', label: 'Preview button prefix', default: 'Preview' },
  { key: 'billing.plans.prorated', section: 'Plans', label: 'Prorated label', default: 'Prorated:' },
  { key: 'billing.plans.effective', section: 'Plans', label: 'Effective label', default: 'Effective' },
  { key: 'billing.plans.continueToCheckout', section: 'Plans', label: 'Continue to checkout button', default: 'Continue to checkout' },
  { key: 'billing.plans.activePlan', section: 'Plans', label: 'Active plan badge', default: 'Active plan' },

  // ── Wallet & credits ─────────────────────────────────────────
  { key: 'billing.wallet.eyebrow', section: 'Wallet & credits', label: 'Wallet eyebrow', default: 'Wallet' },
  { key: 'billing.wallet.title', section: 'Wallet & credits', label: 'Wallet title', default: 'Top up review credits' },
  { key: 'billing.wallet.description', section: 'Wallet & credits', label: 'Wallet description', default: 'Bonus credits scale with the tier amount. Tiers and bonuses are configurable from the platform.', multiline: true },
  { key: 'billing.wallet.payWith', section: 'Wallet & credits', label: 'Pay with label', default: 'Pay with' },
  { key: 'billing.wallet.gateway.stripe', section: 'Wallet & credits', label: 'Stripe label', default: 'Stripe' },
  { key: 'billing.wallet.gateway.paypal', section: 'Wallet & credits', label: 'PayPal label', default: 'PayPal' },
  { key: 'billing.wallet.tiersEmpty', section: 'Wallet & credits', label: 'No tiers text', default: 'Top-up tiers are not configured yet. Please check back shortly or contact support if this persists.', multiline: true },
  { key: 'billing.wallet.topUpUnavailable', section: 'Wallet & credits', label: 'Top up unavailable button', default: 'Top up unavailable' },
  { key: 'billing.wallet.popular', section: 'Wallet & credits', label: 'Popular badge', default: 'Popular' },
  { key: 'billing.wallet.creditsSuffix', section: 'Wallet & credits', label: 'Credits suffix', default: 'credits' },
  { key: 'billing.wallet.bonusSuffix', section: 'Wallet & credits', label: 'Bonus suffix (after "+{n}")', default: 'bonus' },
  { key: 'billing.wallet.processing', section: 'Wallet & credits', label: 'Processing state', default: 'Processing…' },
  { key: 'billing.wallet.couponLabel', section: 'Wallet & credits', label: 'Coupon field label', default: 'Coupon code (optional)' },
  { key: 'billing.wallet.couponPlaceholder', section: 'Wallet & credits', label: 'Coupon placeholder', default: 'WELCOME10' },
  { key: 'billing.wallet.couponHint', section: 'Wallet & credits', label: 'Coupon hint', default: 'Applied to the next validated quote (add-ons and plan changes).', multiline: true },

  // ── Add-ons ──────────────────────────────────────────────────
  { key: 'billing.addons.eyebrow', section: 'Add-ons', label: 'Add-ons eyebrow', default: 'Add-ons' },
  { key: 'billing.addons.title', section: 'Add-ons', label: 'Add-ons title', default: 'Subscription extras compatible with your plan' },
  { key: 'billing.addons.description', section: 'Add-ons', label: 'Add-ons description', default: 'Add-ons follow the same quote → checkout flow as plan changes. Tutor reviews only apply to Writing and Speaking.', multiline: true },
  { key: 'billing.addons.activeOnAccount', section: 'Add-ons', label: 'Active on account heading', default: 'Active on this account' },
  { key: 'billing.addons.creditsSuffix', section: 'Add-ons', label: 'Credits badge suffix', default: 'credits' },
  { key: 'billing.addons.recurring', section: 'Add-ons', label: 'Recurring badge', default: 'Recurring' },
  { key: 'billing.addons.purchaseCredits', section: 'Add-ons', label: 'Purchase credits button', default: 'Purchase credits' },
  { key: 'billing.addons.purchaseAddon', section: 'Add-ons', label: 'Purchase add-on button', default: 'Purchase add-on' },
  { key: 'billing.addons.empty', section: 'Add-ons', label: 'No add-ons text', default: 'No add-ons are compatible with your current plan.' },

  // ── Transactions ─────────────────────────────────────────────
  { key: 'billing.txn.eyebrow', section: 'Transactions', label: 'Transactions eyebrow', default: 'Wallet' },
  { key: 'billing.txn.title', section: 'Transactions', label: 'Transactions title', default: 'Transaction history' },
  { key: 'billing.txn.refresh', section: 'Transactions', label: 'Refresh button', default: 'Refresh' },
  { key: 'billing.txn.empty', section: 'Transactions', label: 'No transactions text', default: 'No transactions yet. Your credit history will appear here.', multiline: true },
  { key: 'billing.txn.balancePrefix', section: 'Transactions', label: 'Balance prefix', default: 'bal:' },

  // ── AI packages ──────────────────────────────────────────────
  { key: 'billing.ai.eyebrow', section: 'AI packages', label: 'AI eyebrow', default: 'AI Credits' },
  { key: 'billing.ai.title', section: 'AI packages', label: 'AI title', default: 'AI grading packages' },
  { key: 'billing.ai.description', section: 'AI packages', label: 'AI description', default: 'Credits grade your Writing letters and Speaking cards instantly with AI. 1 credit = 1 letter or card. Listening & Reading practice is always free. Credits are deducted when grading starts and automatically refunded if grading fails.', multiline: true },
  { key: 'billing.ai.unavailable', section: 'AI packages', label: 'AI unavailable text', default: 'AI packages are not available right now. Please check back shortly.', multiline: true },
  { key: 'billing.ai.toggle.full', section: 'AI packages', label: 'Full Packages toggle', default: 'Full Packages' },
  { key: 'billing.ai.toggle.separate', section: 'AI packages', label: 'Separate Packages toggle', default: 'Separate Packages' },
  { key: 'billing.ai.fullIntro', section: 'AI packages', label: 'Full packages intro', default: 'All-in-one packages bundle AI grading credits. OET Mastery also includes unlimited Listening & Reading practice.', multiline: true },
  { key: 'billing.ai.separateIntro', section: 'AI packages', label: 'Separate packages intro', default: 'Targeted packages focus on a single subtest. Listening & Reading are deterministic and always free to grade.', multiline: true },
  { key: 'billing.ai.fullEmpty', section: 'AI packages', label: 'No full packages text', default: 'No full packages are available yet.' },
  { key: 'billing.ai.priority', section: 'AI packages', label: 'Priority badge', default: 'Priority' },
  { key: 'billing.ai.buyNow', section: 'AI packages', label: 'Buy now button', default: 'Buy now' },
  { key: 'billing.ai.section.listening', section: 'AI packages', label: 'Listening section label', default: 'Listening' },
  { key: 'billing.ai.section.reading', section: 'AI packages', label: 'Reading section label', default: 'Reading' },
  { key: 'billing.ai.section.writing', section: 'AI packages', label: 'Writing section label', default: 'Writing' },
  { key: 'billing.ai.section.speaking', section: 'AI packages', label: 'Speaking section label', default: 'Speaking' },
  { key: 'billing.ai.sectionSuffix', section: 'AI packages', label: 'Section suffix (after subtest)', default: 'Packages' },
  { key: 'billing.ai.mock.eyebrow', section: 'AI packages', label: 'Mock eyebrow', default: 'Mock exams' },
  { key: 'billing.ai.mock.title', section: 'AI packages', label: 'Mock title', default: 'Full mock exam packages' },
  { key: 'billing.ai.mock.description', section: 'AI packages', label: 'Mock description', default: 'Each mock covers all 4 subtests: Writing & Speaking are AI-graded, Listening & Reading are auto-marked. Mock allowances are separate from AI credits.', multiline: true },

  // ── Invoices ─────────────────────────────────────────────────
  { key: 'billing.invoices.eyebrow', section: 'Invoices', label: 'Invoices eyebrow', default: 'Invoices' },
  { key: 'billing.invoices.title', section: 'Invoices', label: 'Invoices title', default: 'Billing history' },
  { key: 'billing.invoices.description', section: 'Invoices', label: 'Invoices description', default: 'Each paid checkout produces an invoice you can download for your records.', multiline: true },
  { key: 'billing.invoices.empty', section: 'Invoices', label: 'No invoices text', default: 'No invoices yet. They will appear after your first paid checkout.', multiline: true },
  { key: 'billing.invoices.download', section: 'Invoices', label: 'Download button', default: 'Download' },
  { key: 'billing.invoices.unavailableNote', section: 'Invoices', label: 'Downloads unavailable note', default: 'Invoice downloads are unavailable on your current plan. Upgrade to enable downloadable invoices.', multiline: true },
];

/** Ordered list of sections for the admin editor (grouping order). */
export const BILLING_COPY_SECTIONS: BillingCopySection[] = [
  'Page & hero',
  'Tabs',
  'Overview',
  'Plans',
  'Wallet & credits',
  'Add-ons',
  'Transactions',
  'AI packages',
  'Invoices',
];

/** key → default value, for the learner-page fallback and the editor's reset-to-default. */
export const BILLING_COPY_DEFAULTS: Record<string, string> = Object.fromEntries(
  BILLING_COPY_FIELDS.map((field) => [field.key, field.default]),
);

/**
 * Build a copy lookup that prefers DB overrides, then in-code defaults, then the key
 * itself only as a last resort (which never happens for known keys). Never returns blank
 * for a known key.
 */
export function makeBillingCopy(overrides: Record<string, string> | null | undefined) {
  return (key: string): string => {
    const override = overrides?.[key];
    if (typeof override === 'string' && override.length > 0) return override;
    return BILLING_COPY_DEFAULTS[key] ?? key;
  };
}
