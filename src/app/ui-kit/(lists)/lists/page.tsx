import React from "react";
import ListsPage from "@/app/ui-kit/(lists)/_components/ListsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Lists - PolytronX",
    description:
      "Explore list components for displaying and organizing content in your React application.",
    keywords: [
      "lists",
      "react components",
      "UI components",
      "content lists",
      "react list",
      "UI list",
      "component customization",
      "react optimization",
      "component performance",
      "list design",
      "UI integration",
      "content organization",
      "list components",
      "display components",
    ],
    openGraph: {
      title: "Lists - PolytronX",
      description:
        "Explore list components for displaying and organizing content in your React application.",
      url: "/ui-kit/lists",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ListsPage />
    </div>
  );
};

export default Page;
