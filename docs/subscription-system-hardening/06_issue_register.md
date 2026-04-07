# Issue Register

| Issue | Impact | Mitigation | Status |
| --- | --- | --- | --- |
| Signup catalog lacked billing plans | Learners could not see published offers early | Added billing plans to the catalog response and signup hook | Resolved |
| Billing page used hardcoded features | Comparison data could drift from the backend | Replaced with a live plan comparison matrix | Resolved |
| Learner nav lacked subscriptions | Billing surface was hard to find | Added `Subscriptions` to the sidebar | Resolved |
| Add-ons were too broad | Users could see irrelevant extras | Filtered add-ons by active-plan compatibility | Resolved |
| Catalog contract had weak coverage | Future regressions could slip through | Added backend contract coverage for `/v1/auth/catalog/signup` | Resolved |
| Checkout return states were unclear | Users could be unsure what happened after payment | Added banner handling for success and failure states | Resolved |
