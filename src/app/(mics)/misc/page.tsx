import React from "react";
import MiscPage from "@/app/(mics)/_components/MiscPage";
import type { Metadata } from "next";

export const dynamic = "force-static";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Miscellaneous - PolytronX",
    description:
      "Explore a collection of miscellaneous React components and utilities for various use cases in your application.",
    keywords: [
      "miscellaneous",
      "react components",
      "UI components",
      "utility components",
      "react misc",
      "UI misc",
      "component customization",
      "react optimization",
      "component performance",
      "misc design",
      "UI integration",
      "utility tools",
      "helper components",
      "various components",
    ],
    openGraph: {
      title: "Miscellaneous - PolytronX",
      description:
        "Explore a collection of miscellaneous React components and utilities for various use cases in your application.",
      url: "/misc",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <MiscPage />
    </div>
  );
};

export default Page;
