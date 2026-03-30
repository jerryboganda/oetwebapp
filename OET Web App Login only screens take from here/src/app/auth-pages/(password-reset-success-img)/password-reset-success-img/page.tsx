import React, { Suspense } from "react";
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
    title: "Password Reset Successful - OET Auth",
    description:
      "Your OET password has been updated successfully. Return to sign in with your new password.",
    openGraph: {
      title: "Password Reset Successful - OET Auth",
      description:
        "Your OET password has been updated successfully. Return to sign in with your new password.",
      url: "/reset-password/success",
      siteName: "OET Auth",
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
    <Suspense fallback={null}>
      <PasswordResetSuccessImgPage />
    </Suspense>
  );
};

export default Page;
