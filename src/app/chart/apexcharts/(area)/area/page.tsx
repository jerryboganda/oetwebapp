import React from "react";
import AreaChartPage from "@/app/chart/apexcharts/(area)/_components/AreaChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Area Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive area charts using ApexCharts for data visualization.",
    keywords: [
      "area chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "area chart customization",
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
      title: "Area Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive area charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/area",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AreaChartPage />
    </div>
  );
};

export default Page;
