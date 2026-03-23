import React from "react";
import { redirect } from "next/navigation";
import type { Metadata } from "next";
import PasswordResetSuccessImgPage from "@/app/auth-pages/(password-reset-success-img)/_components/PasswordResetSuccessImgPage";
import { AUTH_ROUTES } from "@/lib/auth/routes";
import { getResetEmailFromSearchParams } from "@/lib/auth/reset-flow";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Password Reset Successful - PolytronX",
    description:
      "Your OET password has been updated successfully. Return to sign in with your new password.",
    openGraph: {
      title: "Password Reset Successful - PolytronX",
      description:
        "Your OET password has been updated successfully. Return to sign in with your new password.",
      url: "/auth/password-reset-success-img",
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
    redirect(AUTH_ROUTES.signIn);
  }

  return (
    <div>
      <PasswordResetSuccessImgPage />
    </div>
  );
};

export default Page;
