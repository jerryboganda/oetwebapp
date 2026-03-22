import React from "react";
import CheckboxRadioPage from "@/app/forms-elements/(checkbox-radio)/_components/CheckboxRadioPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Checkbox and Radio Forms - PolytronX",
    description:
      "Explore various types of checkbox and radio button components for your React forms.",
    keywords: [
      "checkbox",
      "radio buttons",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "form controls",
      "checkbox customization",
      "radio customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "checkbox validation",
      "radio validation",
    ],
    openGraph: {
      title: "Checkbox and Radio Forms - PolytronX",
      description:
        "Explore various types of checkbox and radio button components for your React forms.",
      url: "/forms-elements/checkbox-radio",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CheckboxRadioPage />
    </div>
  );
};

export default Page;
