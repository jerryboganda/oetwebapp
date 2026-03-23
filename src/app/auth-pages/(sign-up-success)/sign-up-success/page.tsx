import React from "react";
import type { Metadata } from "next";
import SignUpSuccessPage from "@/app/auth-pages/(sign-up-success)/_components/SignUpSuccessPage";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Registration Complete - PolytronX",
    description:
      "Review your OET registration details, subscription snapshot, and next-step actions after creating your account.",
    openGraph: {
      title: "Registration Complete - PolytronX",
      description:
        "Review your OET registration details, subscription snapshot, and next-step actions after creating your account.",
      url: "/auth/sign-up-success",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SignUpSuccessPage />
    </div>
  );
};

export default Page;
