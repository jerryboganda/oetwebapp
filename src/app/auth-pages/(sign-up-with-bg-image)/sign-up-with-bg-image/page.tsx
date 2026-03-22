import React from "react";
import SignUpBgImgPage from "@/app/auth-pages/(sign-up-with-bg-image)/_components/SignUpBgImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Sign Up with Background - PolytronX",
    description:
      "Create your account with our visually appealing registration system featuring a background image.",
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
      "background image",
      "auth background",
      "sign up effects",
      "sign up integration",
      "sign up library",
      "react auth",
      "sign up optimization",
      "sign up performance",
      "user registration",
    ],
    openGraph: {
      title: "Sign Up with Background - PolytronX",
      description:
        "Create your account with our visually appealing registration system featuring a background image.",
      url: "/auth/sign-up-with-bg-image",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SignUpBgImgPage />
    </div>
  );
};

export default Page;
