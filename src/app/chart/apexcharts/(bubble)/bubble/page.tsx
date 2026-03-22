import React from "react";
import BubblePage from "@/app/chart/apexcharts/(bubble)/_components/BubblePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Bubble Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive bubble charts using ApexCharts for data visualization.",
    keywords: [
      "bubble chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "bubble chart customization",
      "scatter plots",
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
      title: "Bubble Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive bubble charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/bubble",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BubblePage />
    </div>
  );
};

export default Page;
