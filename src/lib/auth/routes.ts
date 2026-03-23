export const AUTH_ROUTES = {
  signIn: "/auth-pages/sign-in-with-bg-image",
  signUp: "/auth-pages/sign-up-with-bg-image",
  signUpSuccess: "/auth-pages/sign-up-success",
  passwordReset: "/auth-pages/password-reset-img",
  passwordResetOtp: "/auth-pages/password-reset-otp-img",
  passwordCreate: "/auth-pages/password-create-img",
  passwordResetSuccess: "/auth-pages/password-reset-success-img",
  lockScreen: "/auth-pages/lock-screen-img",
  twoStepVerification: "/auth-pages/two-step-verification-img",
  terms: "/other-pages/terms-condition",
  appHome: "/app/dashboard",
} as const;

export type AuthScreenKey = Exclude<
  keyof typeof AUTH_ROUTES,
  "terms" | "appHome" | "signUpSuccess" | "passwordResetSuccess"
>;

export function getAuthFlowLinks(screen: AuthScreenKey): {
  primary: string;
  secondary: string;
} {
  switch (screen) {
    case "signIn":
      return {
        primary: AUTH_ROUTES.signUp,
        secondary: AUTH_ROUTES.passwordReset,
      };
    case "signUp":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.twoStepVerification,
      };
    case "passwordReset":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.passwordResetOtp,
      };
    case "passwordResetOtp":
      return {
        primary: AUTH_ROUTES.passwordReset,
        secondary: AUTH_ROUTES.passwordCreate,
      };
    case "passwordCreate":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.passwordResetSuccess,
      };
    case "lockScreen":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.passwordReset,
      };
    case "twoStepVerification":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.signUp,
      };
  }
}
