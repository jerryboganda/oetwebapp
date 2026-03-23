import { describe, expect, it } from "vitest";
import { AUTH_ROUTES, getAuthFlowLinks } from "@/lib/auth/routes";

describe("auth route contract", () => {
  it("exposes clean public auth urls as the canonical route contract", () => {
    expect(AUTH_ROUTES.signIn).toBe("/login");
    expect(AUTH_ROUTES.signUp).toBe("/register");
    expect(AUTH_ROUTES.signUpSuccess).toBe("/register/success");
    expect(AUTH_ROUTES.passwordReset).toBe("/forgot-password");
    expect(AUTH_ROUTES.passwordResetOtp).toBe("/forgot-password/verify");
    expect(AUTH_ROUTES.passwordCreate).toBe("/reset-password");
    expect(AUTH_ROUTES.passwordResetSuccess).toBe("/reset-password/success");
    expect(AUTH_ROUTES.lockScreen).toBe("/lock-screen");
    expect(AUTH_ROUTES.twoStepVerification).toBe("/verify");
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
