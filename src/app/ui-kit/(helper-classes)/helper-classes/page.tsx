import React from "react";
import HelperClassPage from "@/app/ui-kit/(helper-classes)/_components/HelperClassPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Helper Classes - PolytronX",
    description:
      "Explore utility classes for styling and layout in your React application.",
    keywords: [
      "helper classes",
      "utility classes",
      "react components",
      "UI components",
      "styling classes",
      "layout classes",
      "react helper",
      "UI helper",
      "component customization",
      "react optimization",
      "component performance",
      "helper design",
      "UI integration",
      "utility components",
      "CSS classes",
    ],
    openGraph: {
      title: "Helper Classes - PolytronX",
      description:
        "Explore utility classes for styling and layout in your React application.",
      url: "/ui-kit/helper-classes",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <HelperClassPage />
    </div>
  );
};

export default Page;
