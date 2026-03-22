import React from "react";
import BoxplotPage from "@/app/chart/apexcharts/(boxplot)/_components/BoxplotPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Boxplot Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive boxplot charts using ApexCharts for statistical data visualization.",
    keywords: [
      "boxplot chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "boxplot customization",
      "statistical charts",
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
      title: "Boxplot Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive boxplot charts using ApexCharts for statistical data visualization.",
      url: "/chart/apexcharts/boxplot",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BoxplotPage />
    </div>
  );
};

export default Page;
