import React from "react";
import TouchSpinPage from "@/app/forms-elements/(touch-spin)/_components/TouchSpinPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Touch Spin Forms - PolytronX",
    description:
      "Explore touch spin components for numeric input with increment/decrement functionality in your React forms.",
    keywords: [
      "touch spin",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "numeric input",
      "number spinner",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "spinner customization",
      "numeric controls",
    ],
    openGraph: {
      title: "Touch Spin Forms - PolytronX",
      description:
        "Explore touch spin components for numeric input with increment/decrement functionality in your React forms.",
      url: "/forms-elements/touch-spin",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TouchSpinPage />
    </div>
  );
};

export default Page;
