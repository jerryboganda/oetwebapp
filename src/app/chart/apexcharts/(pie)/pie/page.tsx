import React from "react";
import PieChartPage from "@/app/chart/apexcharts/(pie)/_components/PieChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Pie Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive pie charts using ApexCharts for data visualization.",
    keywords: [
      "pie chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "pie chart customization",
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
      title: "Pie Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive pie charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/pie",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PieChartPage />
    </div>
  );
};

export default Page;
