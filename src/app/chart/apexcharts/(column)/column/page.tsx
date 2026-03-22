import React from "react";
import ColumnPage from "@/app/chart/apexcharts/(column)/_components/ColumnPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Column Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive column charts using ApexCharts for data visualization.",
    keywords: [
      "column chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "column chart customization",
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
      title: "Column Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive column charts using ApexCharts for data visualization.",
      url: "/chart/apexcharts/column",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ColumnPage />
    </div>
  );
};

export default Page;
