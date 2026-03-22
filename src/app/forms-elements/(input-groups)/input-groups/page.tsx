import React from "react";
import InputGroupsPage from "@/app/forms-elements/(input-groups)/_components/InputGroupsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Input Groups Forms - PolytronX",
    description:
      "Explore input group components for combining form controls with text and icons in your React forms.",
    keywords: [
      "input groups",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "input customization",
      "form layout",
      "group customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "input combination",
      "form controls",
    ],
    openGraph: {
      title: "Input Groups Forms - PolytronX",
      description:
        "Explore input group components for combining form controls with text and icons in your React forms.",
      url: "/forms-elements/input-groups",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <InputGroupsPage />
    </div>
  );
};

export default Page;
