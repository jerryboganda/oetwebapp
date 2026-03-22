import React from "react";
import ReadyToUseFormPage from "@/app/ready-to-use/(ready-to-use-form)/_components/ReadyToUseFormPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Ready to Use Form - PolytronX",
    description:
      "Explore ready-to-use form workspaces with pre-configured components for your React application.",
    keywords: [
      "ready to use form",
      "react forms",
      "UI forms",
      "form components",
      "pre-configured form",
      "form validation",
      "form customization",
      "react optimization",
      "form performance",
      "form design",
      "form integration",
      "form workspace",
      "form components",
      "form library",
    ],
    openGraph: {
      title: "Ready to Use Form - PolytronX",
      description:
        "Explore ready-to-use form workspaces with pre-configured components for your React application.",
      url: "/ready-to-use/ready-to-use-form",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ReadyToUseFormPage />
    </div>
  );
};

export default Page;
