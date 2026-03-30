import React, { Suspense } from "react";
import TwoStepVerificationImgPage from "@/app/auth-pages/(two-step-verification-img)/_components/TwoStepVerificationImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "OTP Verification - OET Auth",
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
      title: "OTP Verification - OET Auth",
      description:
        "Enhance your account security with our visually appealing two-step verification system featuring a background image.",
      url: "/verify",
      siteName: "OET Auth",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <Suspense fallback={null}>
      <TwoStepVerificationImgPage />
    </Suspense>
  );
};

export default Page;
