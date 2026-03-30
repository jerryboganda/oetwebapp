# Integrated Auth System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Firebase and ad hoc panel auth with one ASP.NET Core Identity-based authentication system that supports learner, expert, and admin users, email OTP verification, authenticator-app MFA, refresh tokens, and role-aware access across the Next.js frontend and .NET backend.

**Architecture:** The backend becomes the single source of truth for identity, session issuance, role checks, email verification, MFA enrollment, and token refresh. The Next.js app stops using Firebase entirely and instead uses a shared auth client/provider that talks to new `/v1/auth/*` endpoints, stores access and refresh tokens securely for SPA usage, and gates learner, expert, and admin routes from the current user session returned by the backend.

**Tech Stack:** ASP.NET Core 10, ASP.NET Core Identity, Entity Framework Core, PostgreSQL, JWT bearer auth, refresh tokens, TOTP authenticator apps, SMTP-backed email OTP, Next.js 15, React 19, Vitest, xUnit.

---

## File Structure

### Backend files to create

- `backend/src/OetLearner.Api/Configuration/AuthTokenOptions.cs`
- `backend/src/OetLearner.Api/Configuration/SmtpOptions.cs`
- `backend/src/OetLearner.Api/Contracts/AuthRequests.cs`
- `backend/src/OetLearner.Api/Contracts/AuthResponses.cs`
- `backend/src/OetLearner.Api/Domain/AuthEntities.cs`
- `backend/src/OetLearner.Api/Services/AuthTokenService.cs`
- `backend/src/OetLearner.Api/Services/EmailOtpService.cs`
- `backend/src/OetLearner.Api/Services/AuthService.cs`
- `backend/src/OetLearner.Api/Services/SmtpEmailSender.cs`
- `backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs`
- `backend/src/OetLearner.Api/Data/Migrations/<timestamp>_IntegratedIdentityAuth.cs`

### Backend files to modify

- `backend/src/OetLearner.Api/OetLearner.Api.csproj`
- `backend/src/OetLearner.Api/Program.cs`
- `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- `backend/src/OetLearner.Api/Domain/Entities.cs`
- `backend/src/OetLearner.Api/Domain/ExpertEntities.cs`
- `backend/src/OetLearner.Api/Domain/AdminEntities.cs`
- `backend/src/OetLearner.Api/Services/SeedData.cs`
- `backend/src/OetLearner.Api/appsettings.json`
- `backend/src/OetLearner.Api/appsettings.Development.json`
- `backend/src/OetLearner.Api/appsettings.Production.json`

### Frontend files to create

- `app/(auth)/layout.tsx`
- `app/(auth)/sign-in/page.tsx`
- `app/(auth)/verify-email/page.tsx`
- `app/(auth)/mfa/setup/page.tsx`
- `app/(auth)/mfa/challenge/page.tsx`
- `app/(auth)/forgot-password/page.tsx`
- `app/(auth)/reset-password/page.tsx`
- `components/auth/auth-guard.tsx`
- `components/auth/sign-in-form.tsx`
- `components/auth/email-otp-form.tsx`
- `components/auth/mfa-setup-card.tsx`
- `components/auth/mfa-challenge-form.tsx`
- `lib/auth-client.ts`
- `lib/auth-storage.ts`
- `lib/types/auth.ts`
- `lib/hooks/use-current-user.ts`

### Frontend files to modify

- `app/layout.tsx`
- `app/admin/layout.tsx`
- `app/expert/layout.tsx`
- `contexts/auth-context.tsx`
- `lib/api.ts`
- `lib/env.ts`
- `lib/hooks/use-admin-auth.ts`
- `lib/hooks/use-expert-auth.ts`
- `package.json`
- `lib/__tests__/api.test.ts`

### Test files to create or modify

- `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`
- `backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs`
- `backend/tests/OetLearner.Api.Tests/ExpertFlowsTests.cs`
- `backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs`
- `backend/tests/OetLearner.Api.Tests/Infrastructure/TestWebApplicationFactory.cs`
- `lib/__tests__/auth-client.test.ts`
- `contexts/__tests__/auth-context.test.tsx`

### Deployment and docs files to modify

- `.env.example`
- `.env.production.example`
- `docker-compose.production.yml`
- `DEPLOYMENT.md`
- `README.md`

---

### Task 1: Define the Unified Auth Domain

**Files:**
- Create: `backend/src/OetLearner.Api/Domain/AuthEntities.cs`
- Modify: `backend/src/OetLearner.Api/Domain/Entities.cs`
- Modify: `backend/src/OetLearner.Api/Domain/ExpertEntities.cs`
- Modify: `backend/src/OetLearner.Api/Domain/AdminEntities.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add an xUnit test that expects the database model to contain:
- an application user account with email, password hash, email verification state, MFA state, and role
- refresh tokens
- email OTP challenges
- authenticator recovery codes

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL because the auth entities and DbSets do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create these entities:
- `ApplicationUserAccount`
- `RefreshTokenRecord`
- `EmailOtpChallenge`
- `MfaRecoveryCode`

Rules:
- use GUID/string ids consistent with the repo style
- include `Role` with values `learner`, `expert`, `admin`
- store normalized email and password hash
- store `EmailVerifiedAt`, `AuthenticatorEnabledAt`, `LastLoginAt`
- store hashed refresh tokens, never plaintext

Also link seeded learner, expert, and admin records to an auth account id instead of relying on debug headers.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS for the new domain-model presence test.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Domain/AuthEntities.cs backend/src/OetLearner.Api/Domain/Entities.cs backend/src/OetLearner.Api/Domain/ExpertEntities.cs backend/src/OetLearner.Api/Domain/AdminEntities.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add unified auth domain entities"
```

### Task 2: Wire EF Core and Migration for Auth Storage

**Files:**
- Modify: `backend/src/OetLearner.Api/Data/LearnerDbContext.cs`
- Create: `backend/src/OetLearner.Api/Data/Migrations/<timestamp>_IntegratedIdentityAuth.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add an integration test that creates an auth user and refresh token via `LearnerDbContext` and expects them to persist and round-trip correctly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL because `DbSet`s and mappings are missing.

- [ ] **Step 3: Write minimal implementation**

Add `DbSet`s and indexes for:
- `ApplicationUserAccounts` by normalized email and role
- `RefreshTokenRecords` by user id and expiry
- `EmailOtpChallenges` by user id, purpose, expiry
- `MfaRecoveryCodes` by user id

Generate the migration after the model compiles.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS for persistence tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Data/LearnerDbContext.cs backend/src/OetLearner.Api/Data/Migrations
git commit -m "feat: add auth persistence model"
```

### Task 3: Replace Firebase/Auth Assumptions with Backend Token Options

**Files:**
- Create: `backend/src/OetLearner.Api/Configuration/AuthTokenOptions.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`
- Modify: `backend/src/OetLearner.Api/appsettings.json`
- Modify: `backend/src/OetLearner.Api/appsettings.Development.json`
- Modify: `backend/src/OetLearner.Api/appsettings.Production.json`
- Test: `backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests asserting the app boots with local auth configuration and fails fast when auth token secrets or expiries are missing in non-development environments.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter ProductionReadinessTests`
Expected: FAIL because the new options validation is not in place.

- [ ] **Step 3: Write minimal implementation**

Introduce app options for:
- access token signing key
- refresh token signing key or hashed random token strategy
- issuer
- audience
- access token lifetime
- refresh token lifetime
- OTP lifetime
- authenticator issuer

Update `Program.cs` to:
- remove reliance on external Firebase/JWT authority for first-party auth
- issue and validate the app’s own JWT access tokens
- keep role policies `LearnerOnly`, `ExpertOnly`, `AdminOnly`

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter ProductionReadinessTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Configuration/AuthTokenOptions.cs backend/src/OetLearner.Api/Program.cs backend/src/OetLearner.Api/appsettings.json backend/src/OetLearner.Api/appsettings.Development.json backend/src/OetLearner.Api/appsettings.Production.json backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs
git commit -m "feat: configure first-party token auth"
```

### Task 4: Implement Auth Request and Response Contracts

**Files:**
- Create: `backend/src/OetLearner.Api/Contracts/AuthRequests.cs`
- Create: `backend/src/OetLearner.Api/Contracts/AuthResponses.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add API tests that deserialize the expected shapes for:
- register
- sign-in
- refresh
- current user
- send email verification OTP
- verify email OTP
- begin authenticator setup
- confirm authenticator setup
- challenge MFA
- sign-out

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL because the contracts and endpoints do not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create DTOs with explicit fields and validation-oriented shape.

Minimum request models:
- `RegisterRequest`
- `PasswordSignInRequest`
- `RefreshTokenRequest`
- `SendEmailOtpRequest`
- `VerifyEmailOtpRequest`
- `BeginAuthenticatorSetupRequest`
- `ConfirmAuthenticatorSetupRequest`
- `MfaChallengeRequest`
- `ForgotPasswordRequest`
- `ResetPasswordRequest`

Minimum response models:
- `AuthSessionResponse`
- `CurrentUserResponse`
- `OtpChallengeResponse`
- `AuthenticatorSetupResponse`

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS for serialization contract tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Contracts/AuthRequests.cs backend/src/OetLearner.Api/Contracts/AuthResponses.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: define auth api contracts"
```

### Task 5: Add Email Delivery Abstraction and SMTP-backed OTP Sending

**Files:**
- Create: `backend/src/OetLearner.Api/Configuration/SmtpOptions.cs`
- Create: `backend/src/OetLearner.Api/Services/SmtpEmailSender.cs`
- Create: `backend/src/OetLearner.Api/Services/EmailOtpService.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests that request an email verification OTP and assert:
- a challenge record is created
- the OTP is time-limited
- repeated requests invalidate old OTPs
- the sender is invoked through an abstraction

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL because no OTP service exists.

- [ ] **Step 3: Write minimal implementation**

Create:
- `IEmailSender`-style abstraction or repo-local equivalent
- SMTP sender using env-driven configuration
- OTP generator using cryptographically secure random digits
- hashed OTP persistence, never plaintext

Development rule:
- if SMTP is disabled in development, log OTPs safely for local testing
- production must require SMTP config

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Configuration/SmtpOptions.cs backend/src/OetLearner.Api/Services/SmtpEmailSender.cs backend/src/OetLearner.Api/Services/EmailOtpService.cs backend/src/OetLearner.Api/Program.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add email otp delivery"
```

### Task 6: Implement Password Auth, Refresh Tokens, and Current Session Service

**Files:**
- Create: `backend/src/OetLearner.Api/Services/AuthTokenService.cs`
- Create: `backend/src/OetLearner.Api/Services/AuthService.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests for:
- registering a learner account
- signing in with password
- receiving access + refresh tokens
- refreshing an access token
- signing out and revoking the refresh token
- reading `/v1/auth/me`

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL because the auth service is missing.

- [ ] **Step 3: Write minimal implementation**

Implement:
- password hashing using ASP.NET Core `PasswordHasher<TUser>`
- access token generation with role claims
- opaque refresh token issuance with DB storage and revocation
- current user resolution from token claims
- login audit metadata updates

Use one auth account per human and role claims to decide learner/expert/admin access.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/AuthTokenService.cs backend/src/OetLearner.Api/Services/AuthService.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add core auth service and sessions"
```

### Task 7: Implement Email Verification OTP Flow

**Files:**
- Create: `backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs`
- Modify: `backend/src/OetLearner.Api/Services/AuthService.cs`
- Modify: `backend/src/OetLearner.Api/Program.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add endpoint tests for:
- sending verification OTP after registration
- rejecting invalid OTP
- rejecting expired OTP
- marking email verified on correct OTP
- blocking privileged routes until required verification state is met

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Expose endpoints:
- `POST /v1/auth/register`
- `POST /v1/auth/sign-in`
- `POST /v1/auth/email/send-verification-otp`
- `POST /v1/auth/email/verify-otp`
- `GET /v1/auth/me`
- `POST /v1/auth/refresh`
- `POST /v1/auth/sign-out`

Rule:
- learner sign-in can require verified email before sensitive actions
- expert and admin must require verified email before console access

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs backend/src/OetLearner.Api/Services/AuthService.cs backend/src/OetLearner.Api/Program.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add email verification auth flow"
```

### Task 8: Implement Authenticator App MFA and Recovery Codes

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/AuthService.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests for:
- beginning authenticator setup and receiving secret + otpauth URI
- confirming setup with a valid TOTP code
- requiring MFA challenge on subsequent sign-in
- completing MFA challenge successfully
- using a recovery code once

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Use ASP.NET Core Identity-compatible TOTP generation and verification patterns.

Endpoints:
- `POST /v1/auth/mfa/authenticator/begin`
- `POST /v1/auth/mfa/authenticator/confirm`
- `POST /v1/auth/mfa/challenge`
- `POST /v1/auth/mfa/recovery`

Behavior:
- experts and admins require MFA
- learners may opt in, but plan the model to support mandatory MFA later

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/AuthService.cs backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add authenticator app mfa"
```

### Task 9: Seed Real Auth Accounts for Learner, Expert, and Admin

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/SeedData.cs`
- Modify: `backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs`
- Modify: `backend/tests/OetLearner.Api.Tests/ExpertFlowsTests.cs`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests that assert seeded auth accounts exist for:
- learner
- expert
- admin

and that they map to the correct domain records and roles.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter "AuthFlowsTests|AdminFlowsTests|ExpertFlowsTests"`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Seed auth accounts with:
- role
- verified email state in development
- known local credentials for Docker localhost only

Also create a proper admin auth account because the current code only assumes admin via debug headers.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter "AuthFlowsTests|AdminFlowsTests|ExpertFlowsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/SeedData.cs backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs backend/tests/OetLearner.Api.Tests/ExpertFlowsTests.cs backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: seed unified auth users for all roles"
```

### Task 10: Remove Firebase from the Frontend Runtime

**Files:**
- Delete: `lib/firebase.ts`
- Modify: `package.json`
- Modify: `contexts/auth-context.tsx`
- Modify: `lib/env.ts`
- Modify: `lib/api.ts`
- Modify: `lib/__tests__/api.test.ts`
- Test: `lib/__tests__/auth-client.test.ts`

- [ ] **Step 1: Write the failing test**

Add frontend unit tests proving:
- auth requests no longer import Firebase
- the auth client attaches the backend bearer token
- expired sessions trigger refresh flow

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- auth-client`
Expected: FAIL because the old Firebase-based code still exists.

- [ ] **Step 3: Write minimal implementation**

Remove:
- `firebase` dependency
- Firebase env requirements
- Firebase auth token fetching in `lib/api.ts`

Replace with:
- access token lookup from auth storage
- refresh-on-401 support
- clean unauthenticated state when refresh fails

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- auth-client`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add package.json contexts/auth-context.tsx lib/env.ts lib/api.ts lib/__tests__/api.test.ts lib/__tests__/auth-client.test.ts
git commit -m "refactor: remove firebase frontend auth"
```

### Task 11: Build Shared Frontend Auth Client and Storage

**Files:**
- Create: `lib/types/auth.ts`
- Create: `lib/auth-storage.ts`
- Create: `lib/auth-client.ts`
- Create: `lib/hooks/use-current-user.ts`
- Modify: `contexts/auth-context.tsx`
- Test: `contexts/__tests__/auth-context.test.tsx`

- [ ] **Step 1: Write the failing test**

Add tests for:
- sign-in updates auth context
- sign-out clears storage and state
- `useCurrentUser` exposes role, email verification, and MFA requirements
- automatic `/v1/auth/me` bootstrap on app load

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- auth-context`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Implement one shared auth context that exposes:
- `user`
- `isAuthenticated`
- `isLoading`
- `signIn`
- `signOut`
- `refreshSession`
- `sendEmailOtp`
- `verifyEmailOtp`
- `beginAuthenticatorSetup`
- `confirmAuthenticatorSetup`
- `completeMfaChallenge`

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- auth-context`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add lib/types/auth.ts lib/auth-storage.ts lib/auth-client.ts lib/hooks/use-current-user.ts contexts/auth-context.tsx contexts/__tests__/auth-context.test.tsx
git commit -m "feat: add shared frontend auth client"
```

### Task 12: Add Auth Screens for Sign-in, Email OTP, and MFA

**Files:**
- Create: `app/(auth)/layout.tsx`
- Create: `app/(auth)/sign-in/page.tsx`
- Create: `app/(auth)/verify-email/page.tsx`
- Create: `app/(auth)/mfa/setup/page.tsx`
- Create: `app/(auth)/mfa/challenge/page.tsx`
- Create: `app/(auth)/forgot-password/page.tsx`
- Create: `app/(auth)/reset-password/page.tsx`
- Create: `components/auth/sign-in-form.tsx`
- Create: `components/auth/email-otp-form.tsx`
- Create: `components/auth/mfa-setup-card.tsx`
- Create: `components/auth/mfa-challenge-form.tsx`
- Test: `contexts/__tests__/auth-context.test.tsx`

- [ ] **Step 1: Write the failing test**

Add component tests that assert:
- sign-in submits credentials to the new auth client
- verification page handles OTP success and errors
- MFA setup page renders QR/otpauth URI and confirm form
- MFA challenge page completes login flow

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- auth`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Create an auth flow with these route rules:
- unauthenticated users go to `/sign-in`
- unverified users go to `/verify-email`
- users needing MFA setup go to `/mfa/setup`
- users with pending MFA challenge go to `/mfa/challenge`

Design note:
- keep the UI consistent with the existing project visual style
- use one auth flow for all roles, then route by role after successful session resolution

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- auth`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app/(auth) components/auth contexts/__tests__/auth-context.test.tsx
git commit -m "feat: add auth ui flow"
```

### Task 13: Replace Learner, Expert, and Admin Guards with Unified Session Checks

**Files:**
- Create: `components/auth/auth-guard.tsx`
- Modify: `app/layout.tsx`
- Modify: `app/admin/layout.tsx`
- Modify: `app/expert/layout.tsx`
- Modify: `lib/hooks/use-admin-auth.ts`
- Modify: `lib/hooks/use-expert-auth.ts`
- Test: `contexts/__tests__/auth-context.test.tsx`

- [ ] **Step 1: Write the failing test**

Add tests that assert:
- admin layouts deny non-admin users
- expert layouts deny non-expert users
- learner routes remain accessible to learner accounts
- route guards redirect based on role and verification/MFA state

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- auth`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Replace current behavior:
- `useAdminAuth` currently hardcodes admin access
- `useExpertAuth` currently relies on separate API fetch behavior
- `AuthProvider` currently assumes Firebase/mock logic

with a shared `AuthGuard` that consumes current user state and role claims from `/v1/auth/me`.

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- auth`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add components/auth/auth-guard.tsx app/layout.tsx app/admin/layout.tsx app/expert/layout.tsx lib/hooks/use-admin-auth.ts lib/hooks/use-expert-auth.ts contexts/__tests__/auth-context.test.tsx
git commit -m "refactor: unify role guards on auth session"
```

### Task 14: Update API Client to Use First-Party Tokens Only

**Files:**
- Modify: `lib/api.ts`
- Modify: `lib/types/auth.ts`
- Test: `lib/__tests__/auth-client.test.ts`

- [ ] **Step 1: Write the failing test**

Add tests for:
- bearer token injection from auth storage
- token refresh retry on 401
- sign-out on invalid refresh
- no use of debug headers outside development

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- auth-client`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Refactor the API client to:
- get tokens from `auth-storage`
- refresh access tokens via `/v1/auth/refresh`
- stop using Firebase token acquisition
- keep development debug headers only behind an explicit local-only flag if still needed for non-auth tests

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- auth-client`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add lib/api.ts lib/types/auth.ts lib/__tests__/auth-client.test.ts
git commit -m "feat: switch api client to first-party auth tokens"
```

### Task 15: Add Password Reset and Account Recovery

**Files:**
- Modify: `backend/src/OetLearner.Api/Services/AuthService.cs`
- Modify: `backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs`
- Create: `app/(auth)/forgot-password/page.tsx`
- Create: `app/(auth)/reset-password/page.tsx`
- Test: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`

- [ ] **Step 1: Write the failing test**

Add tests for:
- password reset request
- reset token issuance
- invalid token rejection
- password reset success

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Implement:
- `POST /v1/auth/forgot-password`
- `POST /v1/auth/reset-password`

Use one-time expiring reset tokens delivered by email.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter AuthFlowsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/OetLearner.Api/Services/AuthService.cs backend/src/OetLearner.Api/Endpoints/AuthEndpoints.cs app/(auth)/forgot-password/page.tsx app/(auth)/reset-password/page.tsx backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs
git commit -m "feat: add password reset flow"
```

### Task 16: Update Docker and Environment Configuration

**Files:**
- Modify: `.env.example`
- Modify: `.env.production.example`
- Modify: `docker-compose.production.yml`
- Modify: `DEPLOYMENT.md`
- Modify: `README.md`
- Test: `backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs`

- [ ] **Step 1: Write the failing test**

Add production-readiness tests requiring:
- auth token secrets
- SMTP settings for production
- authenticator issuer
- refresh token settings

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter ProductionReadinessTests`
Expected: FAIL.

- [ ] **Step 3: Write minimal implementation**

Replace Firebase env requirements with:
- `AUTH_TOKEN_ISSUER`
- `AUTH_TOKEN_AUDIENCE`
- `AUTH_ACCESS_TOKEN_SIGNING_KEY`
- `AUTH_REFRESH_TOKEN_DAYS`
- `AUTH_ACCESS_TOKEN_MINUTES`
- `AUTH_OTP_MINUTES`
- `AUTH_AUTHENTICATOR_ISSUER`
- `SMTP_HOST`
- `SMTP_PORT`
- `SMTP_USERNAME`
- `SMTP_PASSWORD`
- `SMTP_FROM_EMAIL`
- `SMTP_FROM_NAME`

Document localhost and VPS Docker behavior.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln --filter ProductionReadinessTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .env.example .env.production.example docker-compose.production.yml DEPLOYMENT.md README.md backend/tests/OetLearner.Api.Tests/ProductionReadinessTests.cs
git commit -m "docs: update deployment config for integrated auth"
```

### Task 17: End-to-End Verification Across All User Types

**Files:**
- Modify: `backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs`
- Modify: `backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs`
- Modify: `backend/tests/OetLearner.Api.Tests/ExpertFlowsTests.cs`
- Modify: `contexts/__tests__/auth-context.test.tsx`

- [ ] **Step 1: Write the failing test**

Add final role-based flow coverage:
- learner login and learner route access
- expert login with verified email + MFA and expert route access
- admin login with verified email + MFA and admin route access

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/OetLearner.sln --filter "AuthFlowsTests|AdminFlowsTests|ExpertFlowsTests"`
Run: `npm test`
Expected: at least one auth regression still failing before final fixes.

- [ ] **Step 3: Write minimal implementation**

Patch any remaining auth edge cases:
- role claim mapping
- redirect loops
- stale refresh token cleanup
- OTP invalidation
- MFA enforcement gaps for admin and expert

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/OetLearner.sln`
Expected: PASS

Run: `npm test`
Expected: PASS

Run: `npm run build`
Expected: PASS

Run: `dotnet build backend/OetLearner.sln`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/tests/OetLearner.Api.Tests/AuthFlowsTests.cs backend/tests/OetLearner.Api.Tests/AdminFlowsTests.cs backend/tests/OetLearner.Api.Tests/ExpertFlowsTests.cs contexts/__tests__/auth-context.test.tsx
git commit -m "test: verify integrated auth across all roles"
```

---

## Integration Notes

- Remove the `firebase` and `firebase-tools` dependencies once no code path uses them.
- Keep development-only debug auth disabled by default once integrated auth is working; if retained temporarily for tests, isolate it behind explicit test configuration only.
- The current admin console has no real admin sign-in today; this plan fixes that by introducing seeded and then production-backed admin accounts.
- The current expert console depends on API auth but still inherits debug-header assumptions in development. This plan removes that split-brain setup.
- Auth should remain first-party and app-owned, even when the AI provider configuration work continues later.
- Email OTP is for verification and recovery; authenticator app MFA is the durable second factor for expert and admin.
- Learner accounts should support MFA enrollment from settings after the shared auth foundation is stable.

## Verification Checklist

- [ ] No Firebase imports remain in the app
- [ ] No Firebase env vars remain in runtime validation
- [ ] Learner sign-in works through `/v1/auth/sign-in`
- [ ] Expert sign-in requires email verification and MFA
- [ ] Admin sign-in requires email verification and MFA
- [ ] Refresh token rotation works
- [ ] Sign-out revokes refresh tokens
- [ ] Password reset works
- [ ] Docker localhost boot works with configured auth env vars
- [ ] Production startup fails fast on missing auth or SMTP secrets
- [ ] `dotnet test backend/OetLearner.sln` passes
- [ ] `npm test` passes
- [ ] `npm run build` passes
- [ ] `dotnet build backend/OetLearner.sln` passes
