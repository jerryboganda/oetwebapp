import React from "react";
import WrapperPage from "@/app/ui-kit/(wrapper)/_components/WrapperPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Wrapper - PolytronX",
    description:
      "Explore wrapper components for layout and content organization in your React application.",
    keywords: [
      "wrapper",
      "react components",
      "UI components",
      "layout wrapper",
      "content wrapper",
      "react wrapper",
      "UI wrapper",
      "component customization",
      "react optimization",
      "component performance",
      "wrapper design",
      "UI integration",
      "layout components",
      "content organization",
    ],
    openGraph: {
      title: "Wrapper - PolytronX",
      description:
        "Explore wrapper components for layout and content organization in your React application.",
      url: "/ui-kit/wrapper",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <WrapperPage />
    </div>
  );
};

export default Page;
