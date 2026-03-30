import React, { Suspense } from "react";
import type { Metadata } from "next";
import SignUpSuccessPage from "@/app/auth-pages/(sign-up-success)/_components/SignUpSuccessPage";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Registration Complete - OET Auth",
    description:
      "Review your OET registration details, subscription snapshot, and next-step actions after creating your account.",
    openGraph: {
      title: "Registration Complete - OET Auth",
      description:
        "Review your OET registration details, subscription snapshot, and next-step actions after creating your account.",
      url: "/register/success",
      siteName: "OET Auth",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <Suspense fallback={null}>
      <SignUpSuccessPage />
    </Suspense>
  );
};

export default Page;
