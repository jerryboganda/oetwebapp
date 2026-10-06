/**
 * Whop identifier checks. Kept apart from the Whop checkout component so a page can ask "is this a
 * Whop plan id?" without importing the Whop SDK packages.
 */

export function isWhopPlanId(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.startsWith('plan_');
}

export function isWhopCheckoutSessionId(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.startsWith('ch_');
}
