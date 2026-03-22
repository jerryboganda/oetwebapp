import React from "react";
import ProgressPage from "@/app/ui-kit/(progress)/_components/ProgressPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Progress - PolytronX",
    description:
      "Explore progress components for visualizing task completion in your React application.",
    keywords: [
      "progress",
      "react components",
      "UI components",
      "task progress",
      "react progress",
      "UI progress",
      "component customization",
      "react optimization",
      "component performance",
      "progress design",
      "UI integration",
      "visualization components",
      "progress bar",
      "task tracking",
    ],
    openGraph: {
      title: "Progress - PolytronX",
      description:
        "Explore progress components for visualizing task completion in your React application.",
      url: "/ui-kit/progress",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ProgressPage />
    </div>
  );
};

export default Page;
