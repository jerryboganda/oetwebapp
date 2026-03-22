import React from "react";
import InputMasksPage from "@/app/forms-elements/(input-masks)/_components/InputMasksPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Input Masks Forms - PolytronX",
    description:
      "Explore input mask components for formatting and validating input data in your React forms.",
    keywords: [
      "input masks",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "data formatting",
      "input validation",
      "mask customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "input patterns",
      "data validation",
    ],
    openGraph: {
      title: "Input Masks Forms - PolytronX",
      description:
        "Explore input mask components for formatting and validating input data in your React forms.",
      url: "/forms-elements/input-masks",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <InputMasksPage />
    </div>
  );
};

export default Page;
