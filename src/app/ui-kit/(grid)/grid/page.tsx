import React from "react";
import GridPage from "@/app/ui-kit/(grid)/_components/GridPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Grid - PolytronX",
    description:
      "Explore grid layout components for responsive and flexible layouts in your React application.",
    keywords: [
      "grid",
      "react components",
      "UI components",
      "layout components",
      "react grid",
      "UI grid",
      "component customization",
      "react optimization",
      "component performance",
      "grid design",
      "UI integration",
      "layout components",
      "responsive grid",
      "flexible layout",
    ],
    openGraph: {
      title: "Grid - PolytronX",
      description:
        "Explore grid layout components for responsive and flexible layouts in your React application.",
      url: "/ui-kit/grid",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <GridPage />
    </div>
  );
};

export default Page;
