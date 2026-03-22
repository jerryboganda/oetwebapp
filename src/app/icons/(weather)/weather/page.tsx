import React from "react";
import WeatherIconPage from "@/app/icons/(weather)/_components/WeatherIconPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Weather Icons - PolytronX",
    description:
      "Explore weather icons for weather and climate representation in your React applications.",
    keywords: [
      "weather icons",
      "react icons",
      "UI icons",
      "icon library",
      "weather symbols",
      "react components",
      "icon customization",
      "icon effects",
      "icon integration",
      "react optimization",
      "icon performance",
      "weather customization",
      "icon styles",
      "icon customization",
    ],
    openGraph: {
      title: "Weather Icons - PolytronX",
      description:
        "Explore weather icons for weather and climate representation in your React applications.",
      url: "/icons/weather",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <WeatherIconPage />
    </div>
  );
};

export default Page;
