# Manual QA Checklist — Round 14 (commit `c426423`)

> Walk this in a real browser on <https://app.oetwithdrhesham.co.uk> with a
> learner account **after** you deploy. Check each item; note anything that
> fails.

## A. Unauth surfaces

- [ ] `/` redirects to `/sign-in` when logged out
- [ ] `/sign-in` renders (no console errors in DevTools)
- [ ] `/exam-guide` loads (should render as SSR — **view source** and confirm
      the hero/cards are in the HTML, not only shown after JS)
- [ ] `/feedback-guide` loads as SSR (same view-source check)

## B. Sign-in

- [ ] Sign in with learner credentials
- [ ] After submit, URL moves off `/sign-in` within ~3s
- [ ] Dashboard `/` shows hero + today priorities (no perpetual skeleton)

## C. Dashboard and core surfaces

- [ ] Notification bell in the top bar shows unread count (if any)
- [ ] Clicking the bell opens the dropdown without errors
- [ ] `/study-plan` loads
- [ ] `/progress` loads
- [ ] `/readiness` loads
- [ ] `/reading` loads
- [ ] `/listening` loads
- [ ] `/writing` loads
- [ ] `/speaking` loads
- [ ] `/mocks` loads
- [ ] `/billing` loads

## D. Performance smell-tests (compared to last session)

- [ ] Skeleton loaders pulse smoothly under slow 3G (DevTools > Network > Slow 3G)
- [ ] Notification bell re-render does NOT flash the whole header on every poll
      (DevTools > Profiler > Record for ~10s idle on the dashboard; the
      `NotificationBellButton` should re-render, but siblings like the logo,
      nav items, user menu should NOT re-render with it)
- [ ] Admin `/admin/users` table still renders normally (no missing rows; no
      layout shift) — this confirms the DataTable refactor didn't regress the
      default code path

## E. Analytics beacons (RSC conversion validation)

Open DevTools > Network, filter `analytics` or `track`.

- [ ] Visiting `/exam-guide` fires **exactly one** `exam_guide_viewed` event
- [ ] Visiting `/feedback-guide` fires **exactly one** `feedback_guide_viewed` event
- [ ] Visiting `/expert/rubric-reference` (if you have expert role) fires
      `expert_rubric_reference_viewed` exactly once

## F. Sign-out

- [ ] Sign-out returns you to `/sign-in`
- [ ] Revisiting `/` redirects back to `/sign-in`

## G. Log the result

Paste the findings in a GitHub issue or reply here:

```text
Round 14 prod verification on c426423
- Unauth: ✅ / ❌
- Sign-in: ✅ / ❌
- Core surfaces: ✅ / ❌
- Perf spot checks: ✅ / ❌
- Analytics: ✅ / ❌
- Sign-out: ✅ / ❌

Notes:
...
```

If **any** box fails, capture a screenshot + the relevant DevTools console/network
panel, and rollback using
[DEPLOY-RUNBOOK-ROUND14.md — section 4](DEPLOY-RUNBOOK-ROUND14.md#4-rollback-only-if-needed).
