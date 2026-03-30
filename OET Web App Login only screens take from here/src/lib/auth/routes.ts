export const AUTH_ROUTES = {
  signIn: "/login",
  signUp: "/register",
  signUpSuccess: "/register/success",
  passwordReset: "/forgot-password",
  passwordResetOtp: "/forgot-password/verify",
  passwordCreate: "/reset-password",
  passwordResetSuccess: "/reset-password/success",
  twoStepVerification: "/verify",
  terms: "/terms",
} as const;

export type AuthScreenKey = Exclude<
  keyof typeof AUTH_ROUTES,
  "terms" | "signUpSuccess" | "passwordResetSuccess"
>;

function withQuery(
  path: string,
  values: Record<string, string | undefined | null>
): string {
  const params = new URLSearchParams();

  Object.entries(values).forEach(([key, value]) => {
    if (value) {
      params.set(key, value);
    }
  });

  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

export function buildSignInHref(options?: {
  status?: string;
  username?: string;
}) {
  return withQuery(AUTH_ROUTES.signIn, {
    status: options?.status,
    username: options?.username,
  });
}

export function buildTwoStepVerificationHref(options?: {
  username?: string;
  error?: string;
}) {
  return withQuery(AUTH_ROUTES.twoStepVerification, {
    username: options?.username,
    error: options?.error,
  });
}

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
