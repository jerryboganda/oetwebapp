export type UserRole = 'learner' | 'expert' | 'admin';
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
}

export interface AuthSession {
  accessToken: string;
  refreshToken: string;
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

export interface SignupSession {
  id: string;
  name: string;
  examTypeId: string;
  professionIds: string[];
  priceLabel: string;
  startDate: string;
  endDate: string;
  deliveryMode: 'online' | 'hybrid' | 'in-person' | string;
  capacity: number;
  seatsRemaining: number;
}

export interface SignupBillingPlan {
  id: string;
  code: string;
  label: string;
  description: string;
  priceAmount: number;
  currency: string;
  interval: string;
  reviewCredits: number;
  displayOrder: number;
  isVisible: boolean;
  isRenewable: boolean;
  trialDays: number;
  includedSubtests: string[];
  entitlements: Record<string, unknown>;
}

export interface SignupCatalog {
  examTypes: SignupExamType[];
  professions: SignupProfession[];
  sessions: SignupSession[];
  billingPlans: SignupBillingPlan[];
  externalAuthProviders: ExternalAuthProvider[];
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
  sessionId: string;
  countryTarget: string;
  agreeToTerms: boolean;
  agreeToPrivacy: boolean;
  marketingOptIn: boolean;
  externalRegistrationToken?: string | null;
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
