"use server";

import { redirect } from "next/navigation";
import {
  AUTH_ROUTES,
  buildSignInHref,
  buildTwoStepVerificationHref,
} from "@/lib/auth/routes";
import {
  buildPasswordCreateHref,
  buildPasswordResetHref,
  buildPasswordResetOtpHref,
  buildPasswordResetSuccessHref,
  normalizeResetEmail,
} from "@/lib/auth/reset-flow";
import { normalizeDemoEmail } from "@/lib/auth/session";

export async function requestPasswordResetOtp(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const email = normalizeResetEmail(String(formData.get("email") ?? ""));

  if (!email) {
    redirect(buildPasswordResetHref({ error: "missing-email" }));
  }

  redirect(
    buildPasswordResetOtpHref({
      email,
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
  const username = normalizeDemoEmail(String(formData.get("username") ?? ""));

  if (!username) {
    redirect(AUTH_ROUTES.signIn);
  }

  redirect(
    buildTwoStepVerificationHref({
      username,
    })
  );
}

export async function loginUser(formData: FormData) {
  await loginAndRedirect(formData);
}

export async function verifyOtp(formData: FormData) {
  await new Promise((res) => setTimeout(res, 500));
  const username = normalizeDemoEmail(String(formData.get("username") ?? ""));
  const otp = ["otp-0", "otp-1", "otp-2", "otp-3", "otp-4"]
    .map((key) => String(formData.get(key) ?? "").trim())
    .join("");

  if (!username) {
    redirect(AUTH_ROUTES.signIn);
  }

  if (otp !== "12345") {
    redirect(
      buildTwoStepVerificationHref({
        username,
        error: "invalid-otp",
      })
    );
  }

  redirect(
    buildSignInHref({
      status: "verified",
      username,
    })
  );
}
