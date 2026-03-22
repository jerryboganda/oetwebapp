import React from "react";
import ScatterChartPage from "@/app/chart/apexcharts/(scatter)/_components/ScatterChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Scatter Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive scatter charts using ApexCharts for data visualization.",
    keywords: [
      "scatter chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "scatter plot customization",
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
      title: "Scatter Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive scatter charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/scatter",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ScatterChartPage />
    </div>
  );
};

export default Page;
