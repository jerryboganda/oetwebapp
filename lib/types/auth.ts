export type UserRole = 'learner' | 'expert' | 'admin' | 'sponsor';
export type ExternalAuthProvider = 'google' | 'facebook' | 'linkedin';

export interface CurrentUser {
  userId: string;
  email: string;
  role: UserRole;
  displayName: string | null;
  isEmailVerified: boolean;
  isAuthenticatorEnabled: boolean;
  requiresEmailVerification: boolean;
  requiresMfa: boolean;
  emailVerifiedAt: string | null;
  authenticatorEnabledAt: string | null;
  adminPermissions?: string[] | null;
  activeProfessionId?: string | null;
  activeProfessionLabel?: string | null;
  /** Relative path of the form `/v1/media/{id}/content`; bearer-authenticated, never load directly in an <img>. */
  avatarUrl?: string | null;
}

export interface AuthSession {
  accessToken: string;
  refreshToken?: string | null;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  currentUser: CurrentUser;
}

export interface OtpChallenge {
  challengeId: string;
  purpose: string;
  deliveryChannel: string;
  destinationHint: string;
  expiresAt: string;
  retryAfterSeconds: number;
}

export interface AuthenticatorSetup {
  secretKey: string;
  otpAuthUri: string;
  qrCodeDataUrl: string;
  recoveryCodes: string[];
}

export interface PendingMfaChallenge {
  email: string;
  challengeToken: string;
  rememberMe: boolean;
}

export interface DeviceSummary {
  id: string;
  maskedDeviceId: string;
  deviceName: string | null;
  platform: string | null;
  trustedAt: string;
  lastSeenAt: string | null;
}

/** Security spec §3.2: mirrors `PendingMfaChallenge` for the device-binding
 * email-OTP challenge (`device_verification_required`). Extended for the
 * two-device default with explicit replacement selection and cooldown evidence. */
export interface PendingDeviceChallenge {
  email: string;
  challengeToken: string;
  rememberMe: boolean;
  mode?: string;
  registeredDevices?: DeviceSummary[];
  activeDeviceCount?: number;
  maxDevices?: number;
  cooldownUntil?: string | null;
  secondsRemaining?: number | null;
  changeWindowDays?: number | null;
  changeMaxPerWindow?: number | null;
  countdown?: string | null;
  selectedDeviceId?: string | null;
  /** Set to `challengeToken` once an OTP has actually been requested for this
   * exact (challenge, selection) pair. Persisted alongside the challenge so
   * the "already sent" guard survives a component remount — e.g. Android
   * backgrounding/killing the WebView while the learner checks their email —
   * instead of living only in a `useRef` that resets on every fresh mount. */
  otpRequestedForToken?: string | null;
}

export interface SignupExamType {
  id: string;
  label: string;
  code: string;
  description: string;
}

export interface SignupProfession {
  id: string;
  label: string;
  countryTargets: string[];
  examTypeIds: string[];
  description: string;
}

export interface SignupCatalog {
  examTypes: SignupExamType[];
  professions: SignupProfession[];
  externalAuthProviders: ExternalAuthProvider[];
  targetCountryOptions: string[];
}

export interface RegisterLearnerInput {
  email: string;
  password: string;
  displayName?: string | null;
  firstName: string;
  lastName: string;
  mobileNumber: string;
  examTypeId: string;
  professionId: string;
  countryTarget: string;
  targetExamDate: string;
  agreeToTerms: boolean;
  agreeToPrivacy: boolean;
  marketingOptIn: boolean;
  externalRegistrationToken?: string | null;
  utmSource?: string | null;
  utmMedium?: string | null;
  utmCampaign?: string | null;
  utmTerm?: string | null;
  utmContent?: string | null;
  referrerUrl?: string | null;
  landingPath?: string | null;
}

export interface ExternalRegistrationPrompt {
  registrationToken: string;
  provider: ExternalAuthProvider;
  email: string;
  firstName: string | null;
  lastName: string | null;
  nextPath: string | null;
}

export type ExternalAuthExchangeResult =
  | { status: 'authenticated'; session: AuthSession; registration: null }
  | { status: 'registration_required'; session: null; registration: ExternalRegistrationPrompt };

export type SignInResult =
  | { status: 'authenticated'; session: AuthSession }
  | { status: 'mfa_required'; challenge: PendingMfaChallenge }
  | { status: 'device_verification_required'; challenge: PendingDeviceChallenge };

/** Narrower than `SignInResult`: completing an MFA challenge/recovery code
 * can never re-request MFA, only succeed outright or (security spec §3.2)
 * bounce into device verification if the account also has a pending device
 * change. Keeping this distinct from `SignInResult` lets callers narrow
 * without an unreachable `mfa_required` branch. */
export type MfaCompletionResult =
  | { status: 'authenticated'; session: AuthSession }
  | { status: 'device_verification_required'; challenge: PendingDeviceChallenge };
