import React from "react";
import RibbonsPage from "@/app/ui-kit/(ribbons)/_components/RibbonsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Ribbons - PolytronX",
    description:
      "Explore ribbon components for decorative and highlight elements in your React application.",
    keywords: [
      "ribbons",
      "react components",
      "UI components",
      "decorative elements",
      "highlight elements",
      "react ribbon",
      "UI ribbon",
      "component customization",
      "react optimization",
      "component performance",
      "ribbon design",
      "UI integration",
      "decoration components",
      "highlight components",
    ],
    openGraph: {
      title: "Ribbons - PolytronX",
      description:
        "Explore ribbon components for decorative and highlight elements in your React application.",
      url: "/ui-kit/ribbons",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <RibbonsPage />
    </div>
  );
};

export default Page;
