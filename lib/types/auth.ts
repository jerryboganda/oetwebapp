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
  nonOetBetaEnabled?: boolean;
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
  | { status: 'mfa_required'; challenge: PendingMfaChallenge };
