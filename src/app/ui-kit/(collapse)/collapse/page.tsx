import React from "react";
import CollapsePage from "@/app/ui-kit/(collapse)/_components/CollapsePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Collapse - PolytronX",
    description:
      "Explore collapse components for showing and hiding content in your React application.",
    keywords: [
      "collapse",
      "react components",
      "UI components",
      "content toggle",
      "react collapse",
      "UI collapse",
      "component customization",
      "react optimization",
      "component performance",
      "collapse design",
      "UI integration",
      "toggle components",
      "hide/show components",
    ],
    openGraph: {
      title: "Collapse - PolytronX",
      description:
        "Explore collapse components for showing and hiding content in your React application.",
      url: "/ui-kit/collapse",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CollapsePage />
    </div>
  );
};

export default Page;
