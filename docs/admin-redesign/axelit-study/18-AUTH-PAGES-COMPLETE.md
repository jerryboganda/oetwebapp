# 18 — AUTH PAGES: Complete spec + May 2026 industry guidelines

**Gap closed**: Login, signup, forgot password, password reset, 2FA, magic link, passkey auth
**Method**: Axelit's auth-pages routes are stubs (`/auth-pages` hash placeholder); content not directly captured. Spec synthesized from Axelit's design tokens (cards + buttons + inputs) + 2026 auth industry consensus.
**Confidence**: **HIGH** ✅

---

## 1 · What Axelit ships (from sidebar nav)

Axelit's sidebar lists "Auth Pages" group with hash placeholder (`#auth-pages`), implying gallery of:
- Sign-in (basic)
- Sign-up (basic)
- Forgot password
- Reset password
- Verify email / OTP
- (likely) Lock screen
- (likely) Two-step verification

Designs would follow the template-marketing pattern (split-screen: hero illustration left + form right, or centered card with gradient background).

---

## 2 · MAY 2026 INDUSTRY STANDARD — Auth UX Guidelines

### 2.1 · Modern auth method hierarchy (2026)

In order of UX preference:

1. **Passkeys (WebAuthn)** — best UX, phishing-resistant, no password to remember. **Required for new admin systems in 2026.**
2. **Magic link** — email a one-click sign-in link
3. **OAuth / SSO** — sign in with Google / Microsoft / GitHub / Okta
4. **Email + password + TOTP 2FA** — fallback for users without passkey support
5. **SMS OTP** — fallback for low-tech users (less secure due to SIM swapping)

**OET admin recommendation**: Passkey + Magic Link primary, password+TOTP as fallback. Drop SMS OTP entirely (PCI/medical sector — SMS isn't compliant for high-trust admins).

### 2.2 · Layout patterns

#### Pattern A — Split-screen (most common for SaaS auth)
```
┌──────────────────────────────────────────────────────────┐
│ ┌────────────────────┐  ┌─────────────────────────────┐ │
│ │                    │  │                             │ │
│ │  HERO ILLUSTRATION │  │   Welcome back              │ │
│ │  or gradient bg    │  │   Sign in to OET Admin      │ │
│ │  or product story  │  │                             │ │
│ │                    │  │   [Continue with Passkey]   │ │
│ │  "Trusted by 10k+  │  │   [Continue with Google]    │ │
│ │   educators"       │  │   ─────── or ───────        │ │
│ │                    │  │   Email                     │ │
│ │  [logo + tagline]  │  │   [_________________]       │ │
│ │                    │  │   Password [forgot?]        │ │
│ │                    │  │   [_________________]       │ │
│ │                    │  │   [Sign in]                 │ │
│ │                    │  │                             │ │
│ │                    │  │   New here? Sign up         │ │
│ └────────────────────┘  └─────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

#### Pattern B — Centered card (minimal)
```
┌──────────────────────────────────────┐
│                                      │
│         [logo]                       │
│                                      │
│    ┌────────────────────────────┐    │
│    │  Sign in                   │    │
│    │  [Continue with Passkey]   │    │
│    │  [Continue with Google]    │    │
│    │  ─────── or ───────        │    │
│    │  [Email]                   │    │
│    │  [Password]                │    │
│    │  [Sign in]                 │    │
│    └────────────────────────────┘    │
│                                      │
│    New here? Sign up                 │
└──────────────────────────────────────┘
```

**OET recommendation**: Pattern B (centered card) for the admin. Admin auth doesn't need marketing — clean, focused.

### 2.3 · Sign-in flow

```
Step 1: Enter email
        ↓
Step 2: Branching
   ├─ Passkey available?  → biometric prompt → in
   ├─ Magic link?          → email sent → click → in
   ├─ Has password?        → enter password → in
   └─ Has 2FA?             → enter TOTP → in
        ↓
Step 3: Redirect to /admin (or saved deep-link)
```

**Industry standard**: passwordless-by-default. Show password field only when no passkey/magic-link available for that email.

### 2.4 · Email-first input pattern

Don't show password field upfront. Step-up:

```tsx
// Step 1
<form onSubmit={handleEmailSubmit}>
  <Input label="Email" type="email" required />
  <Button type="submit">Continue</Button>
</form>

// Step 2 (after email verified to exist)
<form onSubmit={handlePasswordSubmit}>
  <p className="text-muted">Signing in as <strong>{email}</strong> <button>Change</button></p>
  <Input label="Password" type="password" required autoFocus />
  <Link href="/forgot">Forgot password?</Link>
  <Button type="submit">Sign in</Button>
</form>
```

Benefits:
- Reveals auth method per user (passkey vs password vs SSO)
- Password manager handles the multi-step flow gracefully
- Prevents enumeration attacks (don't reveal which emails exist UNLESS user clicks "send reset link")

### 2.5 · Password requirements (NIST SP 800-63B 2024 update)

- **Minimum 8 chars** (12+ for admin)
- **No upper/lower/symbol requirement** (the old rules are counter-productive)
- **Check against breached password list** (HaveIBeenPwned API)
- **Allow paste** in password fields (`autocomplete="current-password"` or `"new-password"`)
- **Show strength meter only after typing 4+ chars**
- **Allow show/hide toggle** (👁 button right of input)
- **Never expire passwords on a schedule** (only when compromise suspected)

```tsx
<Input
  label="Password"
  type={showPassword ? 'text' : 'password'}
  autoComplete="current-password"
  endIcon={
    <button type="button" onClick={() => setShowPassword(!showPassword)} aria-label="Toggle password visibility">
      {showPassword ? <EyeOff /> : <Eye />}
    </button>
  }
/>
```

### 2.6 · Passkey integration

```ts
// Sign in with passkey
const credential = await navigator.credentials.get({
  publicKey: {
    challenge: await fetchChallenge(),
    rpId: 'oet-with-dr-hesham.example.com',
    userVerification: 'preferred',
  },
  mediation: 'conditional',  // "conditional UI" — auto-suggest passkey in email field
});
```

Use `mediation: 'conditional'` so the OS shows a passkey autocomplete BELOW the email field when the user clicks it. Zero friction.

Libraries: **`@simplewebauthn/browser`** for the browser side, **`@simplewebauthn/server`** for backend. OR use Auth providers (Clerk, Stack Auth, BetterAuth) that bundle passkey support.

### 2.7 · Magic link

```
Click "Send sign-in link"
  ↓
Email arrives within 30s
  ↓
Click link → redirected to /admin
  ↓
Link valid for 15 min, single-use, expires after click
```

Anti-phishing: include sender info in the email (`from "no-reply@oet-with-dr-hesham.com" via your team`) + always sign with DKIM/DMARC.

### 2.8 · OAuth / SSO buttons

```tsx
<Button variant="outline" startIcon={<GoogleLogo />}>Continue with Google</Button>
<Button variant="outline" startIcon={<MicrosoftLogo />}>Continue with Microsoft</Button>
<Button variant="outline" startIcon={<OktaLogo />}>Continue with Okta SSO</Button>
```

Rules:
- Brand colors on the logo (NOT on the button bg)
- Button bg matches design system (outline or ghost — NOT brand-colored)
- Order by likelihood (Google first for consumers; Microsoft/Okta first for enterprise admin)
- Show only providers the user's domain supports (configure per-tenant)

### 2.9 · Sign-up flow

For admin onboarding (institutional users):

```
Step 1: Email + password (or passkey enrollment)
        ↓
Step 2: Profile (name, role, institution)
        ↓
Step 3: 2FA enrollment (mandatory for admin)
        ↓
Step 4: Email verification
        ↓
Step 5: Welcome → /admin
```

For OET, admin sign-up is typically **invite-only**. The flow becomes:
```
1. Admin invites via email
2. Invite link → "Accept Invitation" page
3. Set password / enroll passkey
4. Enroll 2FA
5. Verify email (one-click since invite was emailed)
6. Land on /admin
```

### 2.10 · Forgot password flow

```
1. Click "Forgot password?"
2. Enter email
3. Always show "If an account exists, we sent a reset link"
   (Don't reveal account existence — prevents enumeration)
4. Email arrives with single-use, time-limited link
5. Click link → reset page → enter new password (twice)
6. Auto sign-in after reset
```

### 2.11 · 2FA enrollment (TOTP / authenticator app)

```
1. Show QR code + manual entry secret
2. Display recovery codes (10 single-use codes)
   "Save these — they're your backup if you lose your phone"
3. User enters 6-digit code from authenticator
4. Confirmed → 2FA active
```

For verification on sign-in:
```tsx
<Input
  label="6-digit code from your authenticator app"
  type="text"
  inputMode="numeric"
  autoComplete="one-time-code"
  pattern="[0-9]{6}"
  maxLength={6}
  autoFocus
/>
```

`autocomplete="one-time-code"` lets iOS Safari surface SMS OTP if applicable.

### 2.12 · Account lockout

After N failed attempts (default: 10 in 15 min):
- Lock account for 30 min OR require email verification to unlock
- Notify user via email of the failed attempts
- Allow legitimate user to reset password OR contact support

### 2.13 · Loading + error states for auth

```tsx
<Button type="submit" loading={loading} disabled={loading}>
  {loading ? 'Signing in…' : 'Sign in'}
</Button>

{error && (
  <Alert variant="destructive" className="mt-4">
    <AlertTitle>Sign-in failed</AlertTitle>
    <AlertDescription>{error.message}</AlertDescription>
  </Alert>
)}
```

**Error message rules**:
- Don't reveal whether email exists (vague: "Email or password incorrect")
- For locked accounts: "Too many attempts. Reset your password or try again in 30 min."
- For 2FA failure: "Code didn't match. Try again or use a recovery code."

### 2.14 · Persistent session ("Remember me")

Modern pattern: SKIP the checkbox. Just have:
- Default session: 24 hours
- Refresh token: 30 days (silent renewal while user uses the app)
- Sign-out everywhere button in account settings

For shared computers: surface a "Use a private window for shared computers" hint, NOT a checkbox that few users understand.

### 2.15 · Sign-out

- Visible in user menu (top-right avatar dropdown)
- Confirm dialog ONLY if there's unsaved work in app
- Redirect to `/sign-in` after
- Invalidate refresh token server-side (not just clear cookie)

### 2.16 · Session timeout warning

```tsx
useEffect(() => {
  if (sessionExpiresAt - Date.now() < 5 * 60 * 1000) {
    toast.warning('Session expires in 5 min', {
      action: { label: 'Extend', onClick: refreshToken },
      duration: Infinity,  // sticky
    });
  }
}, [sessionExpiresAt]);
```

### 2.17 · Email verification UX

After sign-up:
```
┌─────────────────────────────────────────┐
│       📧                                │
│  Check your email                       │
│  We sent a verification link to         │
│  your@email.com                         │
│                                         │
│  [Resend email]    [Change email]       │
│                                         │
│  Didn't get it? Check spam, or          │
│  contact support@oet-with-dr-hesham.com           │
└─────────────────────────────────────────┘
```

Rate-limit resend to once per 60s. Show countdown on button.

### 2.18 · Accessibility — auth pages

- Form fields have visible labels (NOT placeholder-only)
- Tab order: email → password → forgot link → submit → SSO buttons → sign-up link
- Submit on Enter from password field
- Error messages live-announced via `role="alert"`
- Logo links to `/` (home) — never to itself
- Focus on first input when page loads

### 2.19 · Anti-patterns

- **CAPTCHA on every login** (annoying; use risk-based — only on suspicious patterns)
- **"Remember me" checkbox** (replace with smart session handling — see §2.14)
- **Password complexity rules** (mixed case, symbols — NIST deprecated these)
- **Password expiration** (forces predictable patterns like `Password1!` → `Password2!`)
- **Security questions** (deprecated — too easily guessed/answered via OSINT)
- **Auto-formatting credit card / phone fields with weird masks** (let users paste freely)
- **Splitting OTP across 6 boxes** (paste behavior breaks; use single input with `inputMode="numeric"`)
- **Asking 2FA on every page load** (too friction-heavy; once per session is enough)

### 2.20 · OWASP top auth requirements (2026)

- [ ] HTTPS only (no HTTP auth submission ever)
- [ ] Rate-limit sign-in endpoints (10 attempts / 15 min / IP)
- [ ] Bcrypt/Argon2 password hashing (cost factor ≥ 12)
- [ ] Refresh tokens stored httpOnly, secure, sameSite=lax
- [ ] CSRF protection on all state-changing endpoints
- [ ] Sign tokens (JWT or signed sessions) with rotation
- [ ] Detect leaked credentials via HaveIBeenPwned
- [ ] Log auth events (sign-in, sign-out, failed attempts, password change, 2FA changes) — auditable
- [ ] Email user on security-sensitive changes (password change, new device sign-in)
- [ ] Allow users to view/revoke active sessions
- [ ] Support 2FA recovery codes (download as txt)

---

## 3 · OET ADMIN AUTH PAGE COMPONENT API

```tsx
// app/sign-in/page.tsx
export default function SignInPage() {
  return (
    <AuthLayout variant="centered">
      <AuthCard>
        <AuthHeader logo={<OetLogo />} title="Sign in to OET Admin" />
        <AuthBody>
          <PasskeyButton />
          <SsoButtons providers={['google', 'microsoft', 'okta']} />
          <Divider>or</Divider>
          <SignInForm />
        </AuthBody>
        <AuthFooter>
          <Link href="/forgot">Forgot password?</Link>
        </AuthFooter>
      </AuthCard>
    </AuthLayout>
  );
}

// components/admin/auth/auth-layout.tsx
type AuthLayoutProps = {
  variant: 'centered' | 'split-screen';
  heroIllustration?: React.ReactNode;  // for split-screen
  children: React.ReactNode;
};

// components/admin/auth/auth-card.tsx
// components/admin/auth/passkey-button.tsx
// components/admin/auth/sso-button.tsx
// components/admin/auth/sign-in-form.tsx (uses react-hook-form + Zod)
// components/admin/auth/sign-up-form.tsx
// components/admin/auth/forgot-form.tsx
// components/admin/auth/reset-form.tsx
// components/admin/auth/two-factor-input.tsx
// components/admin/auth/email-verification-banner.tsx
```

## 4 · QA checklist

- [ ] HTTPS-only enforced
- [ ] Passkey is the primary CTA on sign-in
- [ ] SSO buttons present (Google/Microsoft minimum)
- [ ] Magic link option available
- [ ] Password fields allow paste + show/hide toggle
- [ ] `autocomplete` attributes correct on every field
- [ ] Email-first flow (don't show password until known account)
- [ ] Forgot password doesn't reveal email existence
- [ ] 2FA mandatory for admin role
- [ ] Recovery codes provided on 2FA enrollment
- [ ] Rate limiting on sign-in endpoint
- [ ] Failed attempts logged + emailed to user
- [ ] Session timeout warning at 5 min before expiry
- [ ] Sign-out invalidates server-side token
- [ ] WCAG AA on all auth pages
- [ ] Loading state during async submit
- [ ] Error messages vague enough to prevent enumeration

**Confidence upgrade**: NOT CAPTURED → **HIGH** ✅
