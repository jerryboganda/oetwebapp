import React from "react";
import { redirect } from "next/navigation";
import PasswordCreateImgPage from "@/app/auth-pages/(password-create-img)/_components/PasswordCreateImgPage";
import type { Metadata } from "next";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import {
  buildPasswordResetOtpHref,
  getResetEmailFromSearchParams,
  hasVerifiedResetToken,
} from "@/lib/auth/reset-flow";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Create Password with Background - PolytronX",
    description:
      "Create a secure password for your account with our visually appealing password creation system featuring a background image.",
    keywords: [
      "create password",
      "password creation",
      "account setup",
      "react password",
      "UI password",
      "password component",
      "secure password",
      "password creation customization",
      "password styles",
      "background image",
      "auth background",
      "password effects",
      "password integration",
      "password library",
      "react auth",
      "password optimization",
      "password performance",
      "account security",
    ],
    openGraph: {
      title: "Create Password with Background - PolytronX",
      description:
        "Create a secure password for your account with our visually appealing password creation system featuring a background image.",
      url: "/reset-password",
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

  if (!hasVerifiedResetToken(resolvedSearchParams)) {
    redirect(
      buildPasswordResetOtpHref({
        email,
      })
    );
  }

  return (
    <div>
      <PasswordCreateImgPage />
    </div>
  );
};

export default Page;
