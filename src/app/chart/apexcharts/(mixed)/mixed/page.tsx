import React from "react";
import MixedPage from "@/app/chart/apexcharts/(mixed)/_components/MixedPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Mixed Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive mixed charts using ApexCharts for data visualization.",
    keywords: [
      "mixed chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "mixed chart customization",
      "chart styles",
      "chart effects",
      "chart integration",
      "chart library",
      "react chart",
      "chart optimization",
      "chart performance",
      "data representation",
    ],
    openGraph: {
      title: "Mixed Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive mixed charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/mixed",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <MixedPage />
    </div>
  );
};

export default Page;
