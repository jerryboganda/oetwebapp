import React from "react";
import FormWizardsPage from "@/app/ready-to-use/(form-wizards)/_components/FormWizardsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Form Wizards - PolytronX",
    description:
      "Explore multiple form wizards for multi-step form handling in your React application.",
    keywords: [
      "form wizards",
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
      title: "Form Wizards - PolytronX",
      description:
        "Explore multiple form wizards for multi-step form handling in your React application.",
      url: "/ready-to-use/form-wizards",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FormWizardsPage />
    </div>
  );
};

export default Page;
