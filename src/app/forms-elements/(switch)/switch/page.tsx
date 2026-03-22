import React from "react";
import SwitchPage from "@/app/forms-elements/(switch)/_components/SwitchPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Switch Forms - PolytronX",
    description:
      "Explore switch components for toggling boolean values in your React forms.",
    keywords: [
      "switch",
      "form inputs",
      "react forms",
      "UI forms",
      "form components",
      "toggle switch",
      "boolean input",
      "form styles",
      "form integration",
      "react forms library",
      "form optimization",
      "form performance",
      "toggle customization",
      "on/off state",
    ],
    openGraph: {
      title: "Switch Forms - PolytronX",
      description:
        "Explore switch components for toggling boolean values in your React forms.",
      url: "/forms-elements/switch",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SwitchPage />
    </div>
  );
};

export default Page;
