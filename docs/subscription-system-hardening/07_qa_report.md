# QA Report

## Validation Scope

- Frontend lint, unit tests, and build.
- Backend test and build.
- Manual sanity check for subscription navigation, signup plan preview, and payment status banner behavior.

## Expected Coverage

- Signup catalog returns billing plans.
- Registration preview shows published plans from the backend.
- `/subscriptions` renders the billing surface.
- `/billing` remains available as an alias.
- Comparison data is sourced from live plan payloads.

## Execution Status

- Automated validation is part of the implementation closure step.
- Any failing check should be fixed before release.

## Notes

- Webhook-driven subscription reconciliation remains the final authority for plan activation and downgrade timing.
- Checkout redirects should be treated as provisional until the webhook update lands.
