import React from "react";
import RadarChartPage from "@/app/chart/apexcharts/(radar-chart)/_components/RadarChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Radar Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive radar charts using ApexCharts for data visualization.",
    keywords: [
      "radar chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "radar chart customization",
      "spider chart",
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
      title: "Radar Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive radar charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/radar-chart",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <RadarChartPage />
    </div>
  );
};

export default Page;
