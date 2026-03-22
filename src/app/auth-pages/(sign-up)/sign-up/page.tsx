import React from "react";
import SignUpPage from "@/app/auth-pages/(sign-up)/_components/SignUpPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Sign Up - PolytronX",
    description:
      "Create your account with our user-friendly registration system.",
    keywords: [
      "sign up",
      "registration",
      "account creation",
      "react sign up",
      "UI sign up",
      "sign up component",
      "user registration",
      "sign up customization",
      "sign up styles",
      "sign up effects",
      "sign up integration",
      "sign up library",
      "react auth",
      "sign up optimization",
      "sign up performance",
      "user registration",
    ],
    openGraph: {
      title: "Sign Up - PolytronX",
      description:
        "Create your account with our user-friendly registration system.",
      url: "/auth/sign-up",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SignUpPage />
    </div>
  );
};

export default Page;
