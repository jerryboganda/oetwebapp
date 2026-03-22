import React from "react";
import ToursPage from "@/app/advance-ui/(tour)/_components/ToursPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Tour - PolytronX",
    description:
      "Explore advanced tour components and interactive guides for your web applications.",
    keywords: [
      "tour",
      "interactive guide",
      "react tour",
      "UI tour",
      "tour component",
      "interactive walkthrough",
      "tour customization",
      "tour styles",
      "tour effects",
      "tour integration",
      "tour library",
      "react tour guide",
      "tour animation",
      "tour optimization",
      "tour performance",
    ],
    openGraph: {
      title: "Tour - PolytronX",
      description:
        "Explore advanced tour components and interactive guides for your web applications.",
      url: "/advance-ui/tour",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ToursPage />
    </div>
  );
};

export default Page;
