import React from "react";
import Select2Page from "@/app/forms-elements/(select2)/_components/Select2Page";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Select2 Forms - PolytronX",
    description:
      "Explore Select2 components for enhanced dropdown and selection functionality in your React forms.",
    keywords: [
      "select2",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "dropdown customization",
      "select customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "dropdown search",
      "multiple selection",
    ],
    openGraph: {
      title: "Select2 Forms - PolytronX",
      description:
        "Explore Select2 components for enhanced dropdown and selection functionality in your React forms.",
      url: "/forms-elements/select2",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <Select2Page />
    </div>
  );
};

export default Page;
