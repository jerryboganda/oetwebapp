import { describe, expect, it } from "vitest";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";

describe("auth route contract", () => {
  it("keeps the background-image auth pages as the only supported auth routes", () => {
    expect(AUTH_ROUTES.signIn).toBe("/auth-pages/sign-in-with-bg-image");
    expect(AUTH_ROUTES.signUp).toBe("/auth-pages/sign-up-with-bg-image");
    expect(AUTH_ROUTES.passwordReset).toBe("/auth-pages/password-reset-img");
    expect(AUTH_ROUTES.passwordCreate).toBe("/auth-pages/password-create-img");
    expect(AUTH_ROUTES.lockScreen).toBe("/auth-pages/lock-screen-img");
    expect(AUTH_ROUTES.twoStepVerification).toBe(
      "/auth-pages/two-step-verification-img"
    );
  });

  it("returns the expected cross-links for the auth flow", () => {
    expect(getAuthFlowLinks("signIn")).toEqual({
      primary: AUTH_ROUTES.signUp,
      secondary: AUTH_ROUTES.passwordReset,
    });
    expect(getAuthFlowLinks("signUp")).toEqual({
      primary: AUTH_ROUTES.signIn,
      secondary: AUTH_ROUTES.twoStepVerification,
    });
    expect(getAuthFlowLinks("twoStepVerification")).toEqual({
      primary: AUTH_ROUTES.signIn,
      secondary: AUTH_ROUTES.signUp,
    });
  });
});
