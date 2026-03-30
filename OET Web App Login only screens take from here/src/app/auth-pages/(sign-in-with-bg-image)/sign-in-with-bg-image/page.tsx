import React, { Suspense } from "react";
import SignInBgImgPage from "@/app/auth-pages/(sign-in-with-bg-image)/_components/SignInBgImgPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Sign In - OET Auth",
    description:
      "Securely sign in to your account with our visually appealing authentication system featuring a background image.",
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
      "background image",
      "auth background",
      "sign in effects",
      "sign in integration",
      "sign in library",
      "react auth",
      "sign in optimization",
      "sign in performance",
      "user login",
    ],
    openGraph: {
      title: "Sign In - OET Auth",
      description:
        "Securely sign in to your account with our visually appealing authentication system featuring a background image.",
      url: "/login",
      siteName: "OET Auth",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <Suspense fallback={null}>
      <SignInBgImgPage />
    </Suspense>
  );
};

export default Page;
