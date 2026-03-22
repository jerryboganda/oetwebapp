import React from "react";
import PasswordResetPage from "@/app/auth-pages/(password-reset)/_components/PasswordResetPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Password Reset - PolytronX",
    description:
      "Reset your password with our secure and user-friendly password recovery system.",
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
      "password reset effects",
      "password reset integration",
      "password reset library",
      "react auth",
      "password reset optimization",
      "password reset performance",
      "account recovery",
    ],
    openGraph: {
      title: "Password Reset - PolytronX",
      description:
        "Reset your password with our secure and user-friendly password recovery system.",
      url: "/auth/password-reset",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PasswordResetPage />
    </div>
  );
};

export default Page;
