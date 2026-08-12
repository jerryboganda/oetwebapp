# Customer Support Ticket-Linked Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a ticket-linked, candidate-scoped, time-limited customer-support access boundary required by the Listening and Reading v1.1 specification.

**Architecture:** Keep support operators in the existing authenticated admin model. Store a local `CustomerSupportCase` grant with a mandatory external ticket ID, learner ID, expiry, and close state; expose only dedicated permission-gated endpoints and a minimal candidate projection. Every grant lifecycle action and protected candidate read is written to `AuditEvent`.

**Tech Stack:** ASP.NET Core Minimal API, EF Core PostgreSQL, existing `LearnerDbContext`, `ApiException`, admin permission policies, xUnit, EF Core InMemory test provider.

## Global Constraints

- Customer Support access is ticket-linked, candidate-scoped, and time-limited.
- Do not add unrestricted candidate search or assessment-result access.
- Use the existing authenticated admin and granular-permission infrastructure.
- Never expose candidate assessment content in support audit details.
- Preserve unrelated dirty files and do not stage `.codex/config.toml`, `.superpowers/`, `pdf-policy-release/`, or `pdf-policy-release2/`.
- Run only the focused diff check requested by the owner; do not run full CI/CD, deployment, or long validation suites.

---

### Task 1: Add support-case data and dedicated permissions

**Files:**
- Create: `backend/src/OetLearner.Api/Domain/CustomerSupportEntities.cs`
- Modify: `backend/src/OetLearner.Api/Domain/AuthEntities.cs`
- Modify: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- Create: `backend/src/OetLearner.Api/Data/LearnerDbContext.CustomerSupport.cs`
- Create: `backend/src/OetLearner.Api/Data/Migrations/20260904090000_AddCustomerSupportCases.cs`
- Modify: `backend/src/OetLearner.Api/Data/Migrations/LearnerDbContextModelSnapshot.cs`

**Interfaces:**
- Produces `CustomerSupportCase` and `DbSet<CustomerSupportCase> CustomerSupportCases`.
- Produces `AdminPermissions.CustomerSupportRead = "support:ticket_read"` and
  `AdminPermissions.CustomerSupportWrite = "support:ticket_write"`.

- [ ] **Step 1: Define the entity and indexes.** Add the bounded string lengths,
  UTC timestamps, `Status = "open"`, and indexes for external ticket lookup
  and candidate/status/expiry lookup.
- [ ] **Step 2: Register the DbSet and model configuration.** Add the DbSet to
  `LearnerDbContext`; configure the unique external-ticket/candidate index and
  avoid a cascade relationship to application accounts so support grants do
  not make account deletion unsafe.
- [ ] **Step 3: Add granular permission constants.** Include both permissions
  in `AdminPermissions.All` without broadening any existing permission.
- [ ] **Step 4: Generate the EF migration.** Use the repository's existing
  migration tooling so both migration metadata and the model snapshot contain
  the same table/index definitions.

### Task 2: Implement the support-case service

**Files:**
- Create: `backend/src/OetLearner.Api/Services/CustomerSupportCaseService.cs`
- Create: `backend/src/OetLearner.Api/Contracts/CustomerSupportContracts.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`

**Interfaces:**
- `Task<IReadOnlyList<CustomerSupportCaseSummary>> ListAsync(string ticketId, CancellationToken ct)`
- `Task<CustomerSupportCaseSummary> CreateAsync(string actorId, string actorName, CustomerSupportCaseCreateRequest request, CancellationToken ct)`
- `Task<CustomerSupportCandidateProjection> GetCandidateAsync(string actorId, string caseId, CancellationToken ct)`
- `Task<CustomerSupportCaseSummary> CloseAsync(string actorId, string actorName, string caseId, CustomerSupportCaseCloseRequest request, CancellationToken ct)`

- [ ] **Step 1: Add request/response records.** Require ticket ID, candidate
  user ID, subject, and `expiresAt`; return only case metadata and the minimal
  candidate contact fields defined by the design.
- [ ] **Step 2: Validate case creation.** Reject blank/oversized identifiers,
  non-future expiry, missing/deleted/non-learner candidates, and duplicate
  open grants for the same ticket/candidate. Use the existing validation error
  codes and never query candidate data on an invalid ticket boundary.
- [ ] **Step 3: Enforce protected reads.** Resolve by case ID, then verify
  status and server UTC expiry before loading the candidate projection. Return
  not-found for closed or expired grants.
- [ ] **Step 4: Add lifecycle audit events.** Serialize only ticket ID,
  candidate ID, case ID, expiry, and reason; write `support.case.created`,
  `support.case.candidate_read`, and `support.case.closed`.
- [ ] **Step 5: Register the service.** Add one scoped registration in
  `Program.cs`.

### Task 3: Add permission policies and endpoints

**Files:**
- Modify: `backend/src/OetLearner.Api/Program.cs`
- Create: `backend/src/OetLearner.Api/Endpoints/CustomerSupportAdminEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/AdminEndpoints.cs`

**Interfaces:**
- `GET /v1/admin/support/cases?ticketId=...`
- `POST /v1/admin/support/cases`
- `GET /v1/admin/support/cases/{caseId}/candidate`
- `POST /v1/admin/support/cases/{caseId}/close`

- [ ] **Step 1: Add `AdminCustomerSupportRead` and
  `AdminCustomerSupportWrite` policies.** Both require authenticated admin;
  only the matching support permission or `system_admin` satisfies the policy.
- [ ] **Step 2: Map the route group.** Apply the existing admin rate limits,
  the read/write helpers, and no generic `AdminUsersRead` fallback.
- [ ] **Step 3: Add the built-in `customer_support` role metadata.** Give it
  only the two support permissions and include it in the immutable built-in
  role list.
- [ ] **Step 4: Map the endpoint extension in `Program.cs`.** Keep the route
  registration separate from the general admin endpoint file.

### Task 4: Add focused regression coverage and evidence

**Files:**
- Create: `backend/tests/OetLearner.Api.Tests/CustomerSupportCaseServiceTests.cs`
- Modify: `docs/superpowers/evidence/2026-08-11-oet-listening-reading-v1-1-acceptance.md`

- [ ] **Step 1: Add service tests for valid creation and audit details.** Assert
  the grant stores the ticket/candidate/expiry and never stores assessment
  content.
- [ ] **Step 2: Add access-boundary tests.** Assert active projection succeeds,
  expired/closed projection fails, and candidate-only lookup is impossible.
- [ ] **Step 3: Add lifecycle tests.** Assert close is idempotent and writes a
  close audit event with the supplied reason.
- [ ] **Step 4: Update evidence.** Replace the missing support-workflow note
  with the implemented source paths and mark focused execution pending by the
  owner's bounded-validation instruction.
- [ ] **Step 5: Run `git diff --check`, stage only intentional paths, and commit
  the support boundary.** Do not run tests, builds, CI/CD, deployment, or push.

## Self-review coverage

- The PDF role requirement is covered by Tasks 1–4: explicit permission,
  ticket-linked case, candidate-only projection, expiry/revocation, and audit.
- The design deliberately does not claim owner-controlled score tables,
  normalization, graph approval, concurrency targets, or deployed browser
  acceptance; those are separate release gates in the acceptance evidence.
- No external integration, broad PII search, or assessment-result access is
  introduced.
