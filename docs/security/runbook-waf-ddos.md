# WAF, DDoS and Edge Protection Runbook

Security standard §7 (INF-01, INF-02, INF-10, INF-12). Status: **NOT YET VERIFIED** — the controls below are not evidenced. INF-01 and INF-02 are P0 and therefore block release until configured and tested.

Topology today (from `docker-compose.production.yml` and `scripts/deploy/`): **Nginx Proxy Manager terminates TLS** and routes to a single active blue/green upstream (`api-bluegreen.conf.template` selects exactly one of `learner-api-blue`/`learner-api-green`). There is no managed WAF and no CDN/edge absorption layer in front of it.

## INF-01 — Web application firewall

The gap: NPM is a reverse proxy with TLS and basic host routing. It provides no managed rule set, so SQLi/XSS/RCE/known-CVE request patterns reach the API unmediated. Application-level validation and parameterised ORM queries already mitigate the classic injection classes, but INF-01 requires a managed layer as defence in depth.

A compliant option that fits this stack: put a CDN/WAF in front of both `app.oetwithdrhesham.co.uk` and the website, keeping NPM as origin behind an origin-lock.

Configuration checklist:

1. Proxy `app.*`, `api.*` and the marketing site through the WAF provider; NPM becomes origin-only.
2. Enable the provider's managed rule set (OWASP core rules) in **detection/log mode first**. Aggressive enforcement before tuning will break legitimate traffic, not attackers.
3. Restrict origin access to the WAF provider's egress ranges only, so the origin cannot be reached directly by IP. Without this, every WAF rule is trivially bypassed. Confirm by requesting the origin IP directly and expecting a refusal.
4. Custom rate limits, applied per path because the sensitivities differ:
   - `/v1/payment/webhooks/*` — **exclude from rate limiting**. Payment providers deliver bursts and retries; throttling them causes lost fulfilment. This is deliberate and matches the in-app decision to leave webhook endpoints unthrottled.
   - `/v1/billing/checkout-sessions`, `/v1/billing/manual-payments` — per-IP ceilings to blunt card-testing and enumeration.
   - `/v1/auth/*` — credential stuffing and OTP-bombing ceilings (the app also has an `AuthBruteforce` bucket, so the edge limit should be looser than the app's to avoid masking real users behind NAT).
   - `/v1/billing/promo-codes*`, coupon validation — enumeration ceilings.
5. Enable logging and forward WAF logs into the central log store so MON-01/MON-02 alerting covers blocked-pattern spikes.
6. Tune, then switch the managed rule set to blocking. Record the tuning window and the false positives found as evidence.

**Acceptance evidence:** provider config export showing managed rules + custom limits; a direct-to-origin request being refused; a synthetic attack request (e.g. a SQLi payload in a query string) being blocked at the edge; WAF log entries visible in the central store.

## INF-02 — DDoS absorption and emergency modes

1. Enable the provider's L3/L4 and L7 DDoS protection for the proxied hostnames.
2. Define an emergency posture that can be switched on quickly: stricter rate limits, a managed challenge for suspicious clients, and temporary feature restriction. Record the rollback step for each.
3. Verify the origin cannot be saturated by bypassing the edge (this depends on the origin-lock in INF-01 step 3).
4. Confirm the certificate and DNS records survive an edge change (there is no DNSSEC today — see INF-10 below).

**Acceptance evidence:** DDoS protection enabled; a documented emergency mode with a tested rollback; confirmed origin-lock; a load test showing the edge absorbs a burst without origin saturation.

## INF-10 — Domain and DNS

Registrar lock enabled, registrar and DNS accounts on phishing-resistant MFA (IAM-01), least-privilege users, and change alerts on DNS record modification. DNSSEC is worth enabling if the registrar and hosting support it end to end — do not half-enable it, as a broken chain breaks the domain.

## INF-12 — Admin panel separation

The admin surface is served from the same application origin as the learner app (`/admin/**` behind the `AdminOnly` policy). The standard prefers separating it or protecting it with stronger access controls. Practical options, cheapest first:

1. Edge rule requiring a managed challenge or IP allowlist on `/admin/*` in front of the app.
2. A separate admin hostname with its own access policy.
3. Zero-trust gateway in front of `/admin/*`.

Whichever is chosen, keep the existing server-side `AdminOnly` + granular permission policies as the authoritative control — an edge rule is not an authorisation mechanism.

## What is already good (verified in code)

- TLS is terminated at NPM and the proxy topology is deliberate about header trust: `Proxy:TrustForwardHeaders`, `Proxy:KnownNetworks` (loopback + docker bridge CIDRs) and `Proxy:ForwardLimit` (default 2) are configured so a spoofed `X-Forwarded-For` cannot rewrite the client IP used for risk and rate-limit decisions.
- `Proxy:EnforceHttps` defaults on outside Development.
- Single-upstream blue/green cutover means no two API replicas serve simultaneously; the code documents that a Redis backplane must be added before that changes.
