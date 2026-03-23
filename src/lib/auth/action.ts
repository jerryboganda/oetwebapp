"use server";

import { redirect } from "next/navigation";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import {
  buildPasswordCreateHref,
  buildPasswordResetHref,
  buildPasswordResetOtpHref,
  buildPasswordResetSuccessHref,
  normalizeResetEmail,
} from "@/lib/auth/reset-flow";
import {
  clearCurrentSessionUser,
  getCurrentSessionUser,
  setCurrentSessionUser,
} from "@/lib/auth/session.server";
import {
  findMockUserByUsername,
  resolveLoginRedirectPath,
} from "@/lib/auth/session";

export async function unlockScreen(_formData: FormData) {
  const currentUser = await getCurrentSessionUser();
  redirect(
    currentUser
      ? resolveLoginRedirectPath(currentUser.role)
      : AUTH_ROUTES.signIn
  );
}

export async function unlockScreenImg(_formData: FormData) {
  await unlockScreen(_formData);
}

export async function createPassword(_formData: FormData) {
  await completePasswordReset(_formData);
}

export async function resetPassword(_formData: FormData) {
  await requestPasswordResetOtp(_formData);
}

export async function requestPasswordResetOtp(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const email = normalizeResetEmail(String(formData.get("email") ?? ""));

  if (!email) {
    redirect(buildPasswordResetHref({ error: "missing-email" }));
  }

  const user = findMockUserByUsername(email);
  redirect(
    buildPasswordResetOtpHref({
      email: user?.username ?? email,
    })
  );
}

export async function verifyPasswordResetOtp(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const email = normalizeResetEmail(String(formData.get("email") ?? ""));
  const otp = ["otp-0", "otp-1", "otp-2", "otp-3", "otp-4"]
    .map((key) => String(formData.get(key) ?? "").trim())
    .join("");

  if (!email) {
    redirect(buildPasswordResetHref({ error: "missing-email" }));
  }

  if (otp !== "12345") {
    redirect(
      buildPasswordResetOtpHref({
        email,
        error: "invalid-otp",
      })
    );
  }

  redirect(
    buildPasswordCreateHref({
      email,
    })
  );
}

export async function completePasswordReset(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const email = normalizeResetEmail(String(formData.get("email") ?? ""));
  const password = String(formData.get("newPassword") ?? "");
  const confirmPassword = String(formData.get("confirmPassword") ?? "");

  if (!email) {
    redirect(buildPasswordResetHref({ error: "missing-email" }));
  }

  if (password.length < 8) {
    redirect(
      buildPasswordCreateHref({
        email,
        error: "password-too-short",
      })
    );
  }

  if (password !== confirmPassword) {
    redirect(
      buildPasswordCreateHref({
        email,
        error: "password-mismatch",
      })
    );
  }

  redirect(
    buildPasswordResetSuccessHref({
      email,
    })
  );
}

async function loginAndRedirect(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const username = String(formData.get("username") ?? "");
  const user =
    findMockUserByUsername(username) ??
    findMockUserByUsername("learner@oet.app");

  if (user) {
    await setCurrentSessionUser(user.username);
    redirect(resolveLoginRedirectPath(user.role));
  }

  redirect(AUTH_ROUTES.signIn);
}

export async function loginUser(formData: FormData) {
  await loginAndRedirect(formData);
}

export async function loginUserImg(formData: FormData) {
  await loginAndRedirect(formData);
}

export async function verifyOtp(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const username = String(formData.get("username") ?? "");
  const user =
    findMockUserByUsername(username) ??
    findMockUserByUsername("learner@oet.app");

  if (user) {
    await setCurrentSessionUser(user.username);
    redirect(resolveLoginRedirectPath(user.role));
  }

  redirect(AUTH_ROUTES.signIn);
}

export async function logoutUser() {
  await clearCurrentSessionUser();
  redirect(AUTH_ROUTES.signIn);
}
