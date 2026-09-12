# OET Threat Model Outline

- Standard reference: **OET Security Standard v1.0 (12 Sep 2026)**. Control families: `ARC PAY IAM API WEB INF DAT MOB CNT FIL DEV MON AI BCP IR TST`.
- Provenance: family prefixes are used as supplied; concrete `PAY-*` IDs match `payment-audit-findings.md`. Where the standard attaches a number this document has not been able to cross-check, the control is named by family only.

---

## 1. Trust-boundary diagram (text form)

Boundaries are marked `── Bn ──` and numbered outward from the untrusted client. Each boundary is a place where the trust level, the credential, or the party changes.

```
[B0] Untrusted Internet
  │
  ├─ Browser (desktop/mobile web)
  │     ── B1 (TLS; untrusted input → first-party server) ──
  ├─ Next.js app + BFF  (proxy.ts, instrumentation.ts, app/** route handlers, lib/api/**)
  │     server-only secrets live here; browser never holds gateway/session secrets
  │     ── B2 (service credential / internal auth) ──
  ├─ .NET API  (OetLearner.Api: Endpoints/**, Services/**, Security/**)
  │     ── B3 (Least-privilege DB role; parameterised EF Core) ──
  │     ├─ Postgres  (LearnerDbContext; pgvector extension for RAG embeddings)
  │     └─ Object storage  (S3-compatible: media, PDFs, manual-payment proof, uploads)
  │
  │     ── B4 (Outbound third-party APIs over TLS) ──
  ├─ Mail / push / messaging
  │     Brevo SMTP (SmtpEmailSender/BrevoEmailSender), Web Push (WebPushDispatcher),
  │     FCM (MobilePushDispatcher), WhatsApp/SMS (TwilioSmsChannel/WhatsAppChannel)
  │
  │     ── B5 (Provider-side payment state; unauthenticated inbound callbacks) ──
  ├─ Payment gateways
  │     Stripe, PayPal, Whop, Fawaterak, Paymob, EasyKash, PayTabs, Checkout.com
  │     inbound: POST /v1/payment/webhooks/{gateway} (+ GET for easykash/fawaterak)
  │
  │     ── B6 (Model-provider boundary; prompt/inference egress) ──
  ├─ AI providers
  │     agent-gateway (agent-gateway/**), provider registry (Services/Rulebook/AiProviderRegistry.cs),
  │     pgvector RAG (WritingExemplarEmbeddingService, CompanionKnowledgeEntities)
  │
  │     ── B7 (Native shell ↔ web content; OS keychain/secure storage) ──
  └─ Capacitor Android / iOS shells  (android/**, ios/**, capacitor-web/**, capacitor.config.ts)
```

Boundary meanings:

- **B1** browser → Next.js BFF. Untrusted, attacker-controlled input; CSRF/cookie/Origin boundary.
- **B2** BFF → .NET API. Service-to-service; browser must not be able to reach API endpoints it cannot normally reach through the BFF.
- **B3** API → Postgres/object storage. Any SQL/SSRF here is a data-integrity boundary (money, entitlements).
- **B4** API → mail/push. PII egress; the boundary where an injection into a template becomes phishing.
- **B5** gateway ↔ API. **Both directions are attacker-reachable**: inbound callbacks are unauthenticated HTTP until the signature is checked; outbound calls carry provider credentials.
- **B6** API → AI providers. Prompt-injection and data-exfiltration boundary.
- **B7** native shells ↔ web. The web content is only as trusted as the shell's load path; device tokens/secure storage live here.

---

## 2. Per-flow threat list (standard-required flows)

For each flow: **assets** at risk, the **trust boundary** crossed, and the **P0 control IDs** that mitigate it. P0 = blocking for the §17 go-live gate.

### 2.1 Login
- Assets: session/refresh tokens, account existence oracle, MFA state.
- Boundary: B1 (browser → BFF → API `AuthService`, `AuthTokenService`, `Security/TrustedDeviceService`).
- P0 controls: **IAM** (credential + MFA verification, lockout), **API** (rate limiting on credential endpoints), **DAT** (refresh-token hashing/rotation), **MON** (failed-login alerting), **WEB** (secure cookie flags, CSRF).

### 2.2 Password reset
- Assets: reset token, email enumeration, account takeover.
- Boundary: B1 and B4 (email egress).
- P0 controls: **IAM** (single-use, short-TTL, hashed reset token), **API** (rate limiting, no enumeration oracle), **CNT** (email template integrity), **MON** (reset anomaly).

### 2.3 Role change
- Assets: admin privilege, RBAC catalog (`Security/AdminRoleCatalog.cs`, `AdminPermissionEvaluator.cs`).
- Boundary: B2 (admin portal through BFF) → B3 (write to role/permission rows).
- P0 controls: **IAM** (role-change authorisation, separation of duties), **ARC** (least privilege), **API** (admin write policy), **IR** (audit + alerting on privilege escalation), **DAT** (append-only audit record).

### 2.4 Payment
- Assets: money, entitlement grants, `PaymentTransaction`, `CheckoutSession`, `BillingQuote`.
- Boundary: B5 (gateway ↔ API, both directions) plus B1 (checkout initiation).
- P0 controls: **PAY-01..PAY-20** (esp. PAY-01 signature, PAY-03 dedup, PAY-05/06/07/08 binding, PAY-19 reconciliation), **API** (checkout validation), **MON** (divergence alerting).

### 2.5 Subscription activation
- Assets: `CustomerSubscription`, first-period credits, renewal grants.
- Boundary: B5 → B3.
- P0 controls: **PAY** (activation only on verified `completed`), **IAM** (subscription ownership checks on read), **MON** (renewal-failure alerting), **DAT** (idempotent grant ledger).

### 2.6 Content access
- Assets: paid content (papers, video), `ContentAccessService`, `PlanContentAvailabilityService`, video library.
- Boundary: B1/B2 → B3.
- P0 controls: **ARC** (every surface routes through the central visibility gate), **IAM** (entitlement checks, no client-trusted flags), **CNT** (content-protection matrix), **FIL** (media URLs are signed/short-lived).

### 2.7 Refund
- Assets: money out, refund/dispute records, entitlement revocation.
- Boundary: B5 → B3, plus B2 (admin-triggered refunds).
- P0 controls: **PAY** (refund amount reconciliation, no grant on refund), **IAM** (refund requires the dedicated refund permission + step-up), **ARC** (separation of duties), **IR** (audit every refund).

### 2.8 File upload
- Assets: object storage bucket, malware ingress, PII (manual-payment proof), learner documents.
- Boundary: B1 → B2 → B3/object storage.
- P0 controls: **FIL** (type sniffing, size caps, storage path scoping — see `ManualPaymentProof`, `UploadScannerOptions`), **API** (auth + rate limiting), **INF** (bucket private by default, no public listing), **DAT** (no proof PII in logs).

### 2.9 AI grading
- Assets: AI provider credentials, learner answers, grading integrity, cost/credits.
- Boundary: B6 (API → AI providers), plus B3 (pgvector RAG).
- P0 controls: **AI** (provider secret redaction, grounding, budget/credit limits), **DAT** (no learner PII in prompts to third parties where avoidable), **MON** (cost/latency anomaly), **PAY** (AI-grading credits are not granted without payment), **ARC** (feature policy gates).

### 2.10 Admin actions
- Assets: the whole tenant (users, billing, content, roles).
- Boundary: B2 (admin portal) → B3.
- P0 controls: **IAM** (granular permissions, `system_admin` fallback constrained), **ARC** (least privilege, no implicit approval), **API** (per-endpoint write policy), **IR** (immutable audit trail + alerting), **MON** (admin-action anomaly).

### 2.11 Device registration
- Assets: trusted-device tokens, push tokens, native-shell secure storage.
- Boundary: B7 (native shell ↔ web) and B1/B2.
- P0 controls: **MOB** (secure storage/keychain, transport pinning/ATS), **IAM** (device binding to the account, revocation), **DAT** (device-token hashing), **TST** (device-policy verification — `SECURITY-DEVICE-POLICY`).

---

## 3. Boundaries that must be re-verified per release

- **B5** is the highest-risk boundary: unauthenticated inbound + credential-bearing outbound. Any gateway adapter change re-opens PAY-01..PAY-06.
- **B2** — confirm the .NET API is not directly reachable from the browser for admin/learner endpoints that the BFF is meant to broker.
- **B6** — confirm no provider secret or raw learner PII crosses into prompts; confirm RAG retrieval is scoped (`agent-gateway/**`).
- **B7** — confirm native shells do not load remote content with injected JS bridges enabled.
