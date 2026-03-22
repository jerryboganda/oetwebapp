import React from "react";
import ShadowPage from "@/app/ui-kit/(shadow)/_components/ShadowPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Shadow - PolytronX",
    description:
      "Explore shadow components and effects for depth and visual hierarchy in your React application.",
    keywords: [
      "shadow",
      "react components",
      "UI components",
      "visual effects",
      "depth effects",
      "react shadow",
      "UI shadow",
      "component customization",
      "react optimization",
      "component performance",
      "shadow design",
      "UI integration",
      "visual hierarchy",
      "effects components",
    ],
    openGraph: {
      title: "Shadow - PolytronX",
      description:
        "Explore shadow components and effects for depth and visual hierarchy in your React application.",
      url: "/ui-kit/shadow",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ShadowPage />
    </div>
  );
};

export default Page;
