import React from "react";
import FormWizard2Page from "@/app/ready-to-use/(form-wizard-2)/_components/FormWizard2Page";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Form Wizard 2 - PolytronX",
    description:
      "Explore the second form wizard for multi-step form handling in your React application.",
    keywords: [
      "form wizard",
      "react forms",
      "UI forms",
      "form components",
      "multi-step form",
      "form navigation",
      "form validation",
      "form customization",
      "react optimization",
      "form performance",
      "wizard design",
      "form integration",
      "step-by-step",
      "form wizard",
    ],
    openGraph: {
      title: "Form Wizard 2 - PolytronX",
      description:
        "Explore the second form wizard for multi-step form handling in your React application.",
      url: "/ready-to-use/form-wizard-2",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FormWizard2Page />
    </div>
  );
};

export default Page;
