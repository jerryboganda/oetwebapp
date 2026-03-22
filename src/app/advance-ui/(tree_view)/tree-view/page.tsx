import React from "react";
import TreeViewPage from "@/app/advance-ui/(tree_view)/_components/TreeViewPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Tree View - PolytronX",
    description:
      "Explore advanced tree view components and hierarchical data visualization for your web applications.",
    keywords: [
      "tree view",
      "hierarchical data",
      "react tree",
      "UI tree",
      "tree component",
      "data visualization",
      "tree customization",
      "tree styles",
      "tree effects",
      "tree integration",
      "tree library",
      "react tree view",
      "tree animation",
      "tree optimization",
      "tree performance",
    ],
    openGraph: {
      title: "Tree View - PolytronX",
      description:
        "Explore advanced tree view components and hierarchical data visualization for your web applications.",
      url: "/advance-ui/tree-view",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TreeViewPage />
    </div>
  );
};

export default Page;
