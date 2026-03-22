import React from "react";
import RadialbarChartPage from "@/app/chart/apexcharts/(radial-bar)/_components/RadialbarChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Radial Bar Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive radial bar charts using ApexCharts for data visualization.",
    keywords: [
      "radial bar chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "radial bar customization",
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
      title: "Radial Bar Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive radial bar charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/radial-bar",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <RadialbarChartPage />
    </div>
  );
};

export default Page;
