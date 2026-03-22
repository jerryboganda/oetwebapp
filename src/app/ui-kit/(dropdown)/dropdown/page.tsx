import React from "react";
import DropdownPage from "@/app/ui-kit/(dropdown)/_components/DropdownPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Dropdowns - PolytronX",
    description:
      "Explore dropdown components for menu and selection interactions in your React application.",
    keywords: [
      "dropdowns",
      "react components",
      "UI components",
      "menu components",
      "selection components",
      "react dropdown",
      "UI dropdown",
      "component customization",
      "react optimization",
      "component performance",
      "dropdown design",
      "UI integration",
      "menu components",
      "selection components",
      "dropdown menu",
    ],
    openGraph: {
      title: "Dropdowns - PolytronX",
      description:
        "Explore dropdown components for menu and selection interactions in your React application.",
      url: "/ui-kit/dropdown",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <DropdownPage />
    </div>
  );
};

export default Page;
