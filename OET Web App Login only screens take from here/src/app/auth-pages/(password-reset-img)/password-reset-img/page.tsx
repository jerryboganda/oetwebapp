import React, { Suspense } from "react";
import PasswordResetImgPage from "@/app/auth-pages/(password-reset-img)/_components/PasswordResetImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Forgot Password - OET Auth",
    description:
      "Reset your password with our visually appealing password recovery system featuring a background image.",
    keywords: [
      "password reset",
      "password recovery",
      "account recovery",
      "react password reset",
      "UI password reset",
      "password reset component",
      "password recovery",
      "reset password",
      "password reset customization",
      "password reset styles",
      "background image",
      "auth background",
      "password reset effects",
      "password reset integration",
      "password reset library",
      "react auth",
      "password reset optimization",
      "password reset performance",
      "account recovery",
    ],
    openGraph: {
      title: "Forgot Password - OET Auth",
      description:
        "Reset your password with our visually appealing password recovery system featuring a background image.",
      url: "/forgot-password",
      siteName: "OET Auth",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <Suspense fallback={null}>
      <PasswordResetImgPage />
    </Suspense>
  );
};

export default Page;
