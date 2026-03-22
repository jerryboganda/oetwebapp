import React from "react";
import PasswordResetImgPage from "@/app/auth-pages/(password-reset-img)/_components/PasswordResetImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Password Reset with Background - PolytronX",
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
      title: "Password Reset with Background - PolytronX",
      description:
        "Reset your password with our visually appealing password recovery system featuring a background image.",
      url: "/auth/password-reset-img",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PasswordResetImgPage />
    </div>
  );
};

export default Page;
