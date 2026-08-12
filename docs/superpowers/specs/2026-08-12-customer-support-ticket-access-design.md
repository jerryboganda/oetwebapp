# Customer Support Ticket-Linked Access Design

## Goal

Implement the Listening and Reading specification's Customer Support role so
support staff can access only a candidate attached to an explicit ticket and
only until the ticket access grant expires or is revoked.

## Scope

- Add a local support-case boundary with an external ticket identifier.
- Require a candidate user ID and a future expiry for every case.
- Add dedicated read and write permissions for customer-support staff.
- Allow support staff to list cases by ticket, create a case, close/revoke a
  case, and read a minimal candidate contact projection while the case is
  active.
- Audit creation, candidate projection reads, and closure/revocation without
  writing candidate assessment content into the audit payload.
- Preserve the existing admin authentication model; a support operator remains
  an authenticated admin with only the support permissions.

## Non-goals

- No external ticket-provider integration is invented.
- No unrestricted candidate search or candidate-results/attempt projection is
  added for support staff.
- No changes to candidate, tutor, content-author, or assessment-governance
  permissions.

## Data model

`CustomerSupportCase` stores `Id`, `ExternalTicketId`, `CandidateUserId`,
`Subject`, `Status`, `OpenedByAdminId`, `OpenedByAdminName`, `ExpiresAt`,
`ClosedAt`, `CreatedAt`, and `UpdatedAt`. `ExternalTicketId` is indexed
uniquely with the candidate scope, and candidate/status/expiry have a lookup
index. `Status` is `open` or `closed`; expiry is evaluated against the server
clock on every protected read.

## Access rules

1. Creating a case requires `support:ticket_write`, an existing non-deleted
   learner, a non-empty ticket ID, and an expiry strictly after the server's
   current UTC time.
2. Listing cases requires `support:ticket_read` and a ticket ID; it never
   accepts a free-form candidate-only search.
3. Candidate projection requires `support:ticket_read`, an open case, and
   `ExpiresAt > UtcNow`. Closed or expired cases fail closed with a not-found
   response so a stale grant cannot disclose whether a candidate exists.
4. The projection contains only candidate ID, display name, email, account
   status, active profession, and case/ticket expiry metadata. It excludes
   attempts, results, answer keys, billing data, and private content.
5. Closing a case is idempotent and requires `support:ticket_write`; every
   successful state transition is audited with actor, ticket, candidate, and
   reason.

## API

- `GET /v1/admin/support/cases?ticketId=...`
- `POST /v1/admin/support/cases`
- `GET /v1/admin/support/cases/{caseId}/candidate`
- `POST /v1/admin/support/cases/{caseId}/close`

All routes remain under the existing authenticated admin group and use the
dedicated granular policies `AdminCustomerSupportRead` and
`AdminCustomerSupportWrite`.

## Failure handling and audit

Validation errors use the existing `ApiException` contract. Missing, expired,
closed, or mismatched cases do not fall back to direct candidate lookup.
Audit actions are `support.case.created`, `support.case.candidate_read`, and
`support.case.closed`; the candidate projection read is logged separately so
time-limited PII access is observable.

## Verification

The focused backend tests will cover: future-expiry and learner validation,
ticket-scoped listing, candidate projection while active, rejection after
expiry or closure, permission policy names, idempotent close, and audit rows.
The acceptance evidence will remove the current missing-support-workflow
boundary while retaining owner-controlled release-data and runtime-acceptance
boundaries.
