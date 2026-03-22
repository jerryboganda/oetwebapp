import React from "react";
import FloatingLabelPage from "@/app/forms-elements/(floating_labels)/_components/FloatingLabelPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Floating Labels Forms - PolytronX",
    description:
      "Explore floating label components for enhanced form input UX in your React applications.",
    keywords: [
      "floating labels",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "input labels",
      "form UX",
      "label customization",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "label animation",
      "input feedback",
    ],
    openGraph: {
      title: "Floating Labels Forms - PolytronX",
      description:
        "Explore floating label components for enhanced form input UX in your React applications.",
      url: "/forms-elements/floating-labels",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FloatingLabelPage />
    </div>
  );
};

export default Page;
