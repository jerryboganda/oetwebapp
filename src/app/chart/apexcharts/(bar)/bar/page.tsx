import React from "react";
import BarChartPage from "@/app/chart/apexcharts/(bar)/_components/BarChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Bar Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive bar charts using ApexCharts for data visualization.",
    keywords: [
      "bar chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "bar chart customization",
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
      title: "Bar Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive bar charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/bar",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BarChartPage />
    </div>
  );
};

export default Page;
