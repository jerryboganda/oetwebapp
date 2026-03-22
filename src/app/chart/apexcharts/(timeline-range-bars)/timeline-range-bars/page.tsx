import React from "react";
import TimelineChartPage from "@/app/chart/apexcharts/(timeline-range-bars)/_components/TimelineChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Timeline Range Bars Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive timeline range bars charts using ApexCharts for data visualization.",
    keywords: [
      "timeline chart",
      "range bars chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "timeline customization",
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
      title: "Timeline Range Bars Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive timeline range bars charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/timeline-range-bars",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <TimelineChartPage />
    </div>
  );
};

export default Page;
