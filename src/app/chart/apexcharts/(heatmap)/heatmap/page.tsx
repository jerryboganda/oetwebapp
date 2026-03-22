import React from "react";
import HeatmapChartPage from "@/app/chart/apexcharts/(heatmap)/_components/HeatmapChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Heatmap Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive heatmap charts using ApexCharts for data visualization.",
    keywords: [
      "heatmap chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "heatmap customization",
      "color scales",
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
      title: "Heatmap Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive heatmap charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/heatmap",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <HeatmapChartPage />
    </div>
  );
};

export default Page;
