import React from "react";
import TwoStepVerificationPage from "@/app/auth-pages/(two-step-verification)/_components/TwoStepVerificationPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Two-Step Verification - PolytronX",
    description:
      "Enhance your account security with our two-step verification system.",
    keywords: [
      "two-step verification",
      "2FA",
      "two factor authentication",
      "react 2FA",
      "UI 2FA",
      "verification component",
      "account security",
      "two-step customization",
      "2FA styles",
      "2FA effects",
      "2FA integration",
      "2FA library",
      "react auth",
      "2FA optimization",
      "2FA performance",
      "account protection",
    ],
    openGraph: {
      title: "Two-Step Verification - PolytronX",
      description:
        "Enhance your account security with our two-step verification system.",
      url: "/auth/two-step-verification",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TwoStepVerificationPage />
    </div>
  );
};

export default Page;
