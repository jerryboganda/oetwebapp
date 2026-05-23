export const AUTH_ROUTES = {
  signIn: "/sign-in",
  signUp: "/register",
  signUpSuccess: "/register/success",
  passwordReset: "/forgot-password",
  passwordResetOtp: "/forgot-password/verify",
  passwordCreate: "/reset-password",
  passwordResetSuccess: "/reset-password/success",
  twoStepVerification: "/verify-email",
  terms: "/terms",
} as const;

export type AuthScreenKey = Exclude<
  keyof typeof AUTH_ROUTES,
  "terms" | "signUpSuccess" | "passwordResetSuccess"
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
    case "twoStepVerification":
      return {
        primary: AUTH_ROUTES.signIn,
        secondary: AUTH_ROUTES.signUp,
      };
  }
}

export function appendAuthNextParam(href: string, nextPath?: string | null): string {
  const normalizedNext = nextPath?.trim();
  if (!normalizedNext || !normalizedNext.startsWith('/') || normalizedNext.startsWith('//')) {
    return href;
  }

  const [pathname, query = ''] = href.split('?');
  const params = new URLSearchParams(query);
  params.set('next', normalizedNext);

  const nextQuery = params.toString();
  return nextQuery ? `${pathname}?${nextQuery}` : pathname;
}
