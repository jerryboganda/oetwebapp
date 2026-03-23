import React from "react";
import { redirect } from "next/navigation";
import type { Metadata } from "next";
import PasswordResetOtpImgPage from "@/app/auth-pages/(password-reset-otp-img)/_components/PasswordResetOtpImgPage";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import { getResetEmailFromSearchParams } from "@/lib/auth/reset-flow";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Reset OTP Verification - PolytronX",
    description:
      "Verify the email OTP sent for your password reset request in the OET recovery flow.",
    openGraph: {
      title: "Reset OTP Verification - PolytronX",
      description:
        "Verify the email OTP sent for your password reset request in the OET recovery flow.",
      url: "/auth/password-reset-otp-img",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = async ({ searchParams }: PageProps) => {
  const resolvedSearchParams = await searchParams;
  const email = getResetEmailFromSearchParams(resolvedSearchParams);

  if (!email) {
    redirect(AUTH_ROUTES.passwordReset);
  }

  return (
    <div>
      <PasswordResetOtpImgPage />
    </div>
  );
};

export default Page;
