import React from "react";
import TwoStepVerificationImgPage from "@/app/auth-pages/(two-step-verification-img)/_components/TwoStepVerificationImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Two-Step Verification with Background - PolytronX",
    description:
      "Enhance your account security with our visually appealing two-step verification system featuring a background image.",
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
      "background image",
      "auth background",
      "2FA effects",
      "2FA integration",
      "2FA library",
      "react auth",
      "2FA optimization",
      "2FA performance",
      "account protection",
    ],
    openGraph: {
      title: "Two-Step Verification with Background - PolytronX",
      description:
        "Enhance your account security with our visually appealing two-step verification system featuring a background image.",
      url: "/auth/two-step-verification-img",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TwoStepVerificationImgPage />
    </div>
  );
};

export default Page;
