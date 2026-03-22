import React from "react";
import TabsPage from "@/app/ui-kit/(tabs)/_components/TabsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Tabs - PolytronX",
    description:
      "Explore tab components for content organization and navigation in your React application.",
    keywords: [
      "tabs",
      "react components",
      "UI components",
      "content tabs",
      "navigation tabs",
      "react tabs",
      "UI tabs",
      "component customization",
      "react optimization",
      "component performance",
      "tab design",
      "UI integration",
      "navigation components",
      "content organization",
    ],
    openGraph: {
      title: "Tabs - PolytronX",
      description:
        "Explore tab components for content organization and navigation in your React application.",
      url: "/ui-kit/tabs",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TabsPage />
    </div>
  );
};

export default Page;
