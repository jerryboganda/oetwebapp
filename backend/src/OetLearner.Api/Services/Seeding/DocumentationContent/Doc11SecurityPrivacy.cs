using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding.DocumentationContent;

/// <summary>DOC-11 — Security, Privacy, IP &amp; Access-Control Report.</summary>
internal static class Doc11SecurityPrivacy
{
    public static DocumentationModuleSeed Build() => new(
        Code: "DOC-11",
        Title: "Security, Privacy, IP & Access-Control Report",
        Description: "Authentication/OTP/RBAC, secrets management, encryption, retention, prompt-injection defences, and source-code custody — with limitations stated plainly where independent verification does not yet exist.",
        SortOrder: 11,
        Sections:
        [
            new DocumentationSectionBlock(
                "Authentication: password hashing and one-time codes",
                "Password storage uses a custom `IPasswordHasher` implementation, `Pbkdf2Sha512PasswordHasher`, " +
                "rather than the .NET stock hasher — the platform's own security register records the reason: " +
                "PBKDF2-HMAC-SHA512 at at least 220,000 iterations, in an Identity-v3-compatible blob format, was " +
                "verified against the .NET 10 API docs to need a custom implementation because the stock hasher " +
                "has no PRF (pseudorandom function) selector for SHA-512 (EV-SEC-001). A rehash-on-login path " +
                "(`SuccessRehashNeeded`) upgrades any older hash transparently the next time its owner signs in " +
                "(EV-SEC-001). One-time codes (email verification, password reset, device trust) are issued by " +
                "`EmailOtpService` as 6-digit codes with a configurable lifetime, a hard cap of 5 wrong-code " +
                "attempts before the challenge is invalidated, and a server-enforced 60-second resend cooldown " +
                "that a client double-click or network retry cannot bypass (EV-SEC-002). Both the OTP-request and " +
                "password-reset flows return a synthetic, identically-shaped response when the target account " +
                "does not exist, specifically to avoid disclosing account existence through response-shape " +
                "differences (EV-SEC-002)."),
            new DocumentationSectionBlock(
                "Session/device trust and CSRF on cookie-backed sessions",
                "Refresh-token sessions are further guarded by `TrustedDeviceService` and `SignInRiskService` " +
                "(device-trust and sign-in risk signals) and can be revoked centrally via " +
                "`SessionRevocationService` (EV-SEC-003). Where a session's refresh token is held in an HttpOnly " +
                "cookie, `CookieBackedAuthCsrfGuard` enforces double-submit CSRF protection on the mutation: it " +
                "validates the request's Origin/Referer and a matching `x-csrf-token` header against the " +
                "`oet_csrf` cookie, with a documented, narrow bypass only for native app clients that supply an " +
                "explicit body-carried refresh token instead of relying on the cookie (EV-SEC-004). Separately, a " +
                "closed production-readiness gap was hardened and tested: development-mode debug authentication " +
                "headers are computed from `Auth:UseDevelopmentAuth && Environment.IsDevelopment()` and are " +
                "additionally rejected outright by `DevelopmentAuthHandler` outside the Development environment, " +
                "verified by a test that boots a Production host with the development flag left on, sends debug " +
                "admin headers, and asserts a `401` from the admin dashboard (EV-SEC-005)."),
            new DocumentationSectionBlock(
                "Role-based access control: 23 granular admin policies over 11 built-in roles",
                "Admin authorization is not a single \"is-admin\" boolean. The backend registers 23 granular " +
                "authorization policies in `Program.cs` (`AdminContentRead/Write/Publish`, " +
                "`AdminAssessmentGovernanceRead/Write/Approve/Execute`, `AdminBillingRead/Write/RefundWrite/" +
                "CatalogWrite/SubscriptionWrite`, `AdminUsersRead/Write`, `AdminReviewOps`, `AdminAiConfig`, " +
                "`AdminSystemAdmin`, and more), each requiring a specific `admin_permissions` claim value with " +
                "`system_admin` accepted everywhere as an explicit, auditable break-glass override (EV-SEC-006). " +
                "`AdminPermissionEvaluator` centralises the claim-parsing/containment logic so all 23 policies (and " +
                "any in-process service check) share one tested implementation rather than each endpoint " +
                "re-implementing string matching (EV-SEC-007). `AdminRoleCatalog` then packages these permissions " +
                "into 11 named, immutable built-in role presets — for example `Refund Specialist` (read billing " +
                "data, issue refunds/handle disputes, nothing else), `Catalog Editor` (billing read plus catalog " +
                "write only), and `Clinical Reviewer` (content read plus editor-review, explicitly without " +
                "candidate-result access) — kept in the same source file as the permission constants so the role " +
                "list and the enforced permissions cannot silently drift apart (EV-SEC-008). Billing separation of " +
                "duties is enforced at the endpoint level: mark-paid and refund actions require the dedicated " +
                "`AdminBillingMarkPaidWrite`/`AdminBillingRefundWrite` permissions specifically, never the broader " +
                "`billing:write` superset (EV-SEC-009)."),
            new DocumentationSectionBlock(
                "A recent, owner-directed change: per-action step-up removed, sign-in MFA and permission narrowing kept",
                "A specific, dated change to the admin authorization model is recorded factually rather than " +
                "framed as either a strengthening or a weakening: on 2026-09-22, the owner directed removal of " +
                "per-action TOTP step-up re-authentication (`StepUpService`, `WithStepUp`, the " +
                "`/v1/auth/step-up` endpoint, and the admin confirm dialog) that had previously required a fresh " +
                "authenticator code on every individual mark-paid/refund action (EV-SEC-010). What was kept: " +
                "admin sign-in itself still requires a fresh authenticator code once per session where an " +
                "authenticator is configured on the account, and the dedicated `AdminBillingMarkPaidWrite`/" +
                "`AdminBillingRefundWrite` permission narrowing — the separation-of-duties control — remains in " +
                "force and unchanged (EV-SEC-010). The security register records the stated rationale as owner-" +
                "judged friction reduction, since the per-action prompt sat on top of already-required per-session " +
                "MFA, not a removal of authentication or of the permission boundary itself (EV-SEC-010)."),
            new DocumentationSectionBlock(
                "Secrets management and provider-credential redaction",
                "Stored credentials — both platform AI-provider keys (`AiProvider.EncryptedApiKey`) and per-" +
                "account credential-pool keys (`AiProviderAccount.EncryptedApiKey`) — are encrypted at rest via " +
                "ASP.NET Core Data Protection under dedicated purpose strings rather than stored in plain text, " +
                "and are never returned to any client after save; only a masked hint (an ellipsis plus the last " +
                "four characters) is ever shown in the admin UI (EV-SEC-011). This contract is documented as a " +
                "named, test-locked invariant (`RW-019`): list/read API projections are explicit column allow-" +
                "lists so a newly added secret-bearing column cannot leak by omission, audit-log detail strings " +
                "carry only the provider code or account label, and the connectivity-probe error path actively " +
                "strips both the live decrypted key and a list of known third-party credential prefixes (GitHub " +
                "PAT, Anthropic, Google, Slack formats among them) from any error text before it is persisted or " +
                "returned, so a provider that echoes the offending Authorization header back in an error body " +
                "cannot leak it through the platform's own logs or API responses (EV-SEC-011). This contract is " +
                "backed by named xUnit tests covering creation, listing, and key-rotation paths for both the " +
                "provider and account tables (EV-SEC-011)."),
            new DocumentationSectionBlock(
                "No raw file I/O for media or user data",
                "The platform's contributor rules state a hard storage invariant: \"Media/user file I/O must go " +
                "through `IFileStorage` or `S3CompatibleFileStorage`\" and \"Never use raw `File.*`, `Path.*`, or " +
                "`Directory.*` for media/user data\" (EV-SEC-012). `IFileStorage` is a real, narrow interface " +
                "(write/read/exists/delete/move/length/list-keys/resolve-read-url) implemented today by " +
                "`LocalFileStorage`, whose key-resolution logic explicitly rejects absolute, rooted, or UNC-style " +
                "keys and any path segment of `.`/`..`, then re-checks that the fully resolved path still falls " +
                "under the configured storage root before allowing the operation — a concrete path-traversal " +
                "guard on every read, write, and delete rather than an assumed one (EV-SEC-013). Because every " +
                "media/user file path goes through this one abstraction, swapping the backing store (local disk " +
                "today, S3-compatible object storage as the documented next step) requires no change to any " +
                "calling code (EV-SEC-013)."),
            new DocumentationSectionBlock(
                "Data retention and AI response-body custody",
                "Retention is a stated, admin-visible policy rather than an implicit default: raw AI usage " +
                "records (`AiUsageRecord`, one row per physical provider call) are retained for 395 days by " +
                "default, documented as covering a full annual reporting cycle plus a 30-day reconciliation " +
                "window, while aggregate roll-ups are retained for 3,650 days (EV-SEC-014). Full AI response " +
                "bodies are not persisted by default (`StoreResponseBodies=false`); the stated reasoning is that " +
                "audit completeness is already served by hashes, metadata, and the stamped rulebook-grounding " +
                "version, and that storing full response text would create \"a cross-user data-leakage surface, " +
                "an unnecessary retention obligation, and a larger PII footprint\" — enabling it requires adding " +
                "sampling and a consent gate, not a config flip alone (EV-SEC-014)."),
            new DocumentationSectionBlock(
                "Prompt-injection and AI-abuse defences",
                "The platform's threat model names the AI-provider boundary explicitly (\"B6 — Model-provider " +
                "boundary; prompt/inference egress\") and lists it as a \"Prompt-injection and data-exfiltration " +
                "boundary\" requiring per-release re-verification that no provider secret or raw learner PII " +
                "crosses into a prompt and that retrieval-augmented-generation lookups stay properly scoped " +
                "(EV-SEC-015). The independent-pentest scope document mandates prompt injection via learner input " +
                "reaching system tools, provider-secret exposure, and AI cost abuse as required test areas " +
                "(EV-SEC-016). Structurally, the platform's own mandatory grounding contract is itself a defence: " +
                "`AiGatewayService` refuses to forward any request whose system prompt lacks the fixed rulebook-" +
                "grounding header, which closes off a whole class of \"raw prompt\" injection paths by " +
                "construction rather than by input filtering alone (see DOC-03). A second, narrower layer — the " +
                "TypeSafe SystemOne (\"Jev\") pre-gateway Writing guard — runs a parallel judgment specifically for " +
                "injection attempts, rule-evasion, abuse, and gibberish input before a Writing submission reaches " +
                "the grading gateway; it is documented as a negative gate only, meaning it can block or flag a " +
                "submission for tutor review but can never itself grant a pass (EV-SEC-017)."),
            new DocumentationSectionBlock(
                "Independent verification: stated honestly as not yet complete",
                "This pack states plainly, per its own evidence-only rule, that no independently verified, " +
                "completed third-party penetration test exists in the repository as of this report. What does " +
                "exist is a scoped, written engagement plan (`docs/security/pentest-scope.md`) naming eleven in-" +
                "scope targets (public website, learner web app, API/backend, admin portal, payment flows, " +
                "subscriptions, roles/authorisation, file upload, business logic, and the Android/iOS apps), a " +
                "mandated test matrix (`pv-matrix.md`, PV-01..PV-20, covering replay, forgery, IDOR/BOLA, mass " +
                "assignment, price/currency manipulation, and race conditions across all eight supported payment " +
                "gateways), and formal rules of engagement including a sandbox-only default, a no-data-" +
                "exfiltration rule, and a named stopping condition (EV-SEC-018). The security evidence pack's own " +
                "machine-readable control register records every one of the twenty PV test cases as `unexecuted` " +
                "and its overall release-gate verdict as `BLOCKED`, because at least one payment control was found " +
                "failing and several others were unverified at the time of that pass (EV-SEC-019). This is " +
                "recorded here as the honest current state, not softened: an independent pentest is planned and " +
                "scoped, but has not yet run and has not yet produced a report to attach."),
            new DocumentationSectionBlock(
                "Source-code and IP custody",
                "The engineering repository that this pack cites throughout is held privately, and the platform's " +
                "own operational rules treat that as the default custody posture — its GitHub Actions compute " +
                "policy explicitly instructs flipping the repository's visibility back to private once a public-" +
                "for-CI window is verified closed, rather than leaving it public (EV-SEC-020). Beyond that " +
                "operational custody fact, this pack does not find a dedicated intellectual-property assignment " +
                "agreement, contributor licence agreement, or LICENSE file inside the repository establishing " +
                "formal chain-of-title for the codebase; this is stated here as a limitation rather than implied " +
                "away. The commissioning and ownership chain that does exist in checkable, dated form — who " +
                "directs the product, who leads the engineering team, and the founder's own dated clinical-" +
                "content rulings embedded in the governance layer — is documented in full in DOC-02 (Founder, " +
                "Product Origin & Innovation Ownership Statement) and is not repeated here."),
        ],
        Evidence:
        [
            new DocumentationEvidenceSeed("EV-SEC-001", DocumentationEvidenceType.Security,
                "Custom PBKDF2-HMAC-SHA512 (>=220k iterations) IPasswordHasher, chosen because the .NET stock hasher has no PRF selector, plus rehash-on-login migration.",
                "backend/src/OetLearner.Api/Security/Pbkdf2Sha512PasswordHasher.cs; backend/src/OetLearner.Api/Security/PasswordHasherPolicy.cs; docs/security/control-register.json (IAM-01)"),
            new DocumentationEvidenceSeed("EV-SEC-002", DocumentationEvidenceType.Security,
                "OTP issuance service: 6-digit codes, configurable lifetime, 5-attempt hard cap, 60-second server-enforced resend cooldown, and account-existence non-disclosure via a masked synthetic response.",
                "backend/src/OetLearner.Api/Services/EmailOtpService.cs"),
            new DocumentationEvidenceSeed("EV-SEC-003", DocumentationEvidenceType.Security,
                "Device-trust, sign-in risk, and session revocation services backing cookie-based sessions.",
                "backend/src/OetLearner.Api/Security/TrustedDeviceService.cs; backend/src/OetLearner.Api/Security/SignInRiskService.cs; backend/src/OetLearner.Api/Security/SessionRevocationService.cs"),
            new DocumentationEvidenceSeed("EV-SEC-004", DocumentationEvidenceType.Security,
                "Double-submit CSRF guard for cookie-backed refresh-token mutations, with a documented native-client body-token bypass.",
                "backend/src/OetLearner.Api/Security/CookieBackedAuthCsrfGuard.cs"),
            new DocumentationEvidenceSeed("EV-SEC-005", DocumentationEvidenceType.Testing,
                "Production dev-auth guard: development debug headers are computed from an environment-gated flag and independently rejected outside Development, verified by a Production-host test asserting 401 on the admin dashboard.",
                "docs/security/admin-rbac-policy-mapping.md, section \"Production Dev-Auth Guard\"; backend/src/OetLearner.Api/Security/DevelopmentAuthHandler.cs"),
            new DocumentationEvidenceSeed("EV-SEC-006", DocumentationEvidenceType.Security,
                "23 registered granular admin authorization policies (content, assessment-governance, billing, users, review-ops, AI config, system-admin), each requiring a specific admin_permissions claim with system_admin as the universal break-glass override.",
                "docs/security/admin-rbac-policy-mapping.md, section \"Registered Policies\""),
            new DocumentationEvidenceSeed("EV-SEC-007", DocumentationEvidenceType.Code,
                "Centralised, case-insensitive admin-permission claim evaluator shared by all registered policies and by in-process service checks.",
                "backend/src/OetLearner.Api/Security/AdminPermissionEvaluator.cs"),
            new DocumentationEvidenceSeed("EV-SEC-008", DocumentationEvidenceType.Security,
                "11 immutable built-in admin role presets (System Admin, Content Author, Clinical Reviewer, Language Assessor, Reviewer, Billing Admin, Customer Support, Refund Specialist, Catalog Editor, Subscription Manager, legacy Content Editor) kept in the same file as the permission constants.",
                "backend/src/OetLearner.Api/Security/AdminRoleCatalog.cs"),
            new DocumentationEvidenceSeed("EV-SEC-009", DocumentationEvidenceType.Security,
                "Billing separation-of-duties: mark-paid and refund endpoints require the dedicated AdminBillingMarkPaidWrite/AdminBillingRefundWrite permissions, never the broad billing:write superset.",
                "docs/security/control-register.json (PAY-20 status_reason); docs/security/admin-rbac-policy-mapping.md, section \"Billing And Freeze Operations\""),
            new DocumentationEvidenceSeed("EV-SEC-010", DocumentationEvidenceType.Security,
                "Owner-directed removal (2026-09-22) of per-action TOTP step-up on admin mark-paid/refund actions, with per-session authenticator MFA at sign-in and the dedicated permission narrowing both retained.",
                "docs/security/README.md, \"Ordered fix list\" item 5 (PAY-20); docs/security/control-register.json (PAY-20 row, status \"waived_by_owner\")"),
            new DocumentationEvidenceSeed("EV-SEC-011", DocumentationEvidenceType.Security,
                "Provider-credential secret-redaction contract: encryption at rest, masked hints only, allow-listed read projections, redacted audit details, and connectivity-probe error redaction of live keys and known third-party credential prefixes, backed by named xUnit tests.",
                "docs/security/ai-provider-secret-redaction.md"),
            new DocumentationEvidenceSeed("EV-SEC-012", DocumentationEvidenceType.Architecture,
                "Standing contributor rule that media/user file I/O must go through IFileStorage/S3CompatibleFileStorage and never raw File.*/Path.*/Directory.* calls.",
                "AGENTS.md, section \"Storage Persistence\""),
            new DocumentationEvidenceSeed("EV-SEC-013", DocumentationEvidenceType.Code,
                "IFileStorage abstraction and its LocalFileStorage implementation, including explicit path-traversal guards (rejecting rooted/UNC keys and '.'/'..' segments, then re-validating the resolved path stays under the storage root).",
                "backend/src/OetLearner.Api/Services/Content/IFileStorage.cs"),
            new DocumentationEvidenceSeed("EV-SEC-014", DocumentationEvidenceType.DataKnowledge,
                "Documented retention defaults (395 days raw AiUsageRecord, 3650 days aggregates) and the StoreResponseBodies=false default with its stated data-minimisation rationale.",
                "docs/AI-USAGE-POLICY.md, sections 8 \"Custody & security options\" and 9 \"Observability & alerting\""),
            new DocumentationEvidenceSeed("EV-SEC-015", DocumentationEvidenceType.Security,
                "Threat-model trust-boundary diagram naming the AI-provider boundary (B6) as a prompt-injection and data-exfiltration boundary requiring per-release re-verification.",
                "docs/security/threat-model-outline.md, sections 1 and 3"),
            new DocumentationEvidenceSeed("EV-SEC-016", DocumentationEvidenceType.Security,
                "Pentest scope mandate naming AI prompt injection, provider-secret exposure, and cost abuse as required test areas.",
                "docs/security/pentest-scope.md, section 2.4 \"Other\""),
            new DocumentationEvidenceSeed("EV-SEC-017", DocumentationEvidenceType.AiModel,
                "TypeSafe SystemOne (Jev) pre-gateway Writing guard: a parallel judgment for injection/rule-evasion/abuse/gibberish, documented as a negative gate only that can block or flag but never grant a pass.",
                "docs/AI-USAGE-POLICY.md, \"Direct (non-gateway) AI calls\" table (jev.writing.guard row) and \"TypeSafe SystemOne (Jev)\" note"),
            new DocumentationEvidenceSeed("EV-SEC-018", DocumentationEvidenceType.Security,
                "Written, scoped independent-pentest engagement plan: in-scope targets, mandated PV-01..PV-20 test matrix, and formal rules of engagement — not yet an executed/reported test.",
                "docs/security/pentest-scope.md"),
            new DocumentationEvidenceSeed("EV-SEC-019", DocumentationEvidenceType.Security,
                "Machine-readable control register recording all 20 PV test cases as unexecuted and the overall §17 go-live gate verdict as BLOCKED at the time of the recorded evidence pass.",
                "docs/security/control-register.json (pv_tests array); docs/security/README.md, \"Verdict\""),
            new DocumentationEvidenceSeed("EV-SEC-020", DocumentationEvidenceType.Deployment,
                "Standing operational rule that the engineering repository's default custody posture is private, with any public-for-CI window explicitly required to be flipped back to private once verified.",
                "AGENTS.md, section \"GitHub Actions on a public-when-working repo — COMPULSORY\""),
        ]);
}
