import React from "react";
import CandlestickChartPage from "@/app/chart/apexcharts/(candlestick)/_components/CandlestickChartPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Candlestick Chart - ApexCharts - PolytronX",
    description:
      "Explore interactive candlestick charts using ApexCharts for financial data visualization.",
    keywords: [
      "candlestick chart",
      "apexcharts",
      "charts",
      "data visualization",
      "react charts",
      "UI charts",
      "chart component",
      "candlestick customization",
      "financial charts",
      "stock charts",
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
      title: "Candlestick Chart - ApexCharts - PolytronX",
      description:
        "Explore interactive candlestick charts using ApexCharts for financial data visualization.",
      url: "/chart/apexcharts/candlestick",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <CandlestickChartPage />
    </div>
  );
};

export default Page;
