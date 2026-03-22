import React from "react";
import TextareaPage from "@/app/forms-elements/(textarea)/_components/TextareaPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Textarea Forms - PolytronX",
    description:
      "Explore textarea components for multi-line text input in your React forms.",
    keywords: [
      "textarea",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "text input",
      "multi-line input",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "text formatting",
      "input customization",
    ],
    openGraph: {
      title: "Textarea Forms - PolytronX",
      description:
        "Explore textarea components for multi-line text input in your React forms.",
      url: "/forms-elements/textarea",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TextareaPage />
    </div>
  );
};

export default Page;
