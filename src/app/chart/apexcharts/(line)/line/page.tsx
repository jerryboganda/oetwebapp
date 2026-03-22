import React from "react";
import LinePage from "@/app/chart/apexcharts/(line)/_components/LinePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Line Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive line charts using ApexCharts for data visualization.",
    keywords: [
      "line chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "line chart customization",
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
      title: "Line Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive line charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/line",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <LinePage />
    </div>
  );
};

export default Page;
