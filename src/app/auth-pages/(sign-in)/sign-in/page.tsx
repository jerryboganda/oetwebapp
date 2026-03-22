import React from "react";
import SignInPage from "@/app/auth-pages/(sign-in)/_components/SignInPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Sign In - PolytronX",
    description:
      "Securely sign in to your account with our user-friendly authentication system.",
    keywords: [
      "sign in",
      "login",
      "authentication",
      "react sign in",
      "UI sign in",
      "sign in component",
      "user authentication",
      "sign in customization",
      "sign in styles",
      "sign in effects",
      "sign in integration",
      "sign in library",
      "react auth",
      "sign in optimization",
      "sign in performance",
      "user login",
    ],
    openGraph: {
      title: "Sign In - PolytronX",
      description:
        "Securely sign in to your account with our user-friendly authentication system.",
      url: "/auth/sign-in",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SignInPage />
    </div>
  );
};

export default Page;
