# Notification Module Progress

## Shipped foundation

- Backend event catalog covers learner, expert, admin, billing, study, mock, speaking, writing, account, and lifecycle events.
- Notification domain includes events, inbox items, preferences, policy overrides, delivery attempts, push subscriptions, push tokens, consents, and suppressions.
- User APIs support feed, read state, preferences, push subscriptions, mobile push token registration, and consent updates.
- Admin APIs support catalog, policies, delivery health, deliveries, consents, suppressions, test email, and proof trigger.
- Admin UI exposes notification policy, delivery health, deliveries, and test email surfaces.
- Reminder settings now reuse the shared notification preferences panel instead of fake local-only state.
- Notification proof failures no longer write unmanaged temp logs with recipient email or raw exception details.
- Web Push configuration now routes through runtime settings.

## Remaining launch blockers

- SMS dispatcher and provider webhook status ingestion are not production-proven.
- WhatsApp dispatcher and template approval workflow are not production-proven.
- Campaigns, segments, approvals, A/B tests, and lifecycle automation dashboards are not production-complete.
- Cost dashboards, channel spend caps, and provider outage alerts need admin evidence for all external channels.
- Schools, sponsors, and minors need explicit consent/privacy gates before external-channel rollout.
- Browser evidence is still needed for admin policy editing, user preferences, proof trigger, suppression, and delivery-failure health.

## Validation ledger

- `npm run backend:build` passes after disabling the .NET terminal logger in the backend npm scripts.
- Focused notification-adjacent frontend test passed for `/settings/reminders` after wiring it to shared preferences.

