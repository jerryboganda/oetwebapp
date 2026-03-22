import React from "react";
import PasswordCreateImgPage from "@/app/auth-pages/(password-create-img)/_components/PasswordCreateImgPage";
import type { Metadata } from "next";

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
      url: "/auth/password-create-img",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PasswordCreateImgPage />
    </div>
  );
};

export default Page;
