import React from "react";
import TermsPage from "@/app/other-pages/(terms-condition)/_components/TermsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Terms & Conditions - PolytronX",
    description:
      "Terms and conditions page for your React application's legal terms and user agreements.",
    keywords: [
      "terms and conditions",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "legal page",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "terms design",
      "page structure",
      "platform integration",
      "user agreement",
      "legal terms",
    ],
    openGraph: {
      title: "Terms & Conditions - PolytronX",
      description:
        "Terms and conditions page for your React application's legal terms and user agreements.",
      url: "/other-pages/terms-condition",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TermsPage />
    </div>
  );
};

export default Page;
