import React from "react";
import PrivacyPolicyPage from "@/app/other-pages/(privacy-policy)/_components/PrivacyPolicyPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Privacy Policy - PolytronX",
    description:
      "Privacy policy page for your React application's data protection and user privacy information.",
    keywords: [
      "privacy policy",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "legal page",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "privacy design",
      "page structure",
      "platform integration",
      "data protection",
      "user privacy",
    ],
    openGraph: {
      title: "Privacy Policy - PolytronX",
      description:
        "Privacy policy page for your React application's data protection and user privacy information.",
      url: "/other-pages/privacy-policy",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PrivacyPolicyPage />
    </div>
  );
};

export default Page;
