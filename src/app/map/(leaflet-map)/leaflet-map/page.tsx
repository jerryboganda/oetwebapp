import React from "react";
import LeafletMapPage from "@/app/map/(leaflet-map)/_components/LeafletMapClient";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Leaflet Maps - PolytronX",
    description:
      "Explore Leaflet Maps integration for your React applications with interactive maps and geolocation features.",
    keywords: [
      "leaflet maps",
      "react maps",
      "UI maps",
      "map integration",
      "react components",
      "map customization",
      "geolocation",
      "map markers",
      "map styles",
      "map optimization",
      "map performance",
      "map customization",
      "map effects",
      "map integration",
    ],
    openGraph: {
      title: "Leaflet Maps - PolytronX",
      description:
        "Explore Leaflet Maps integration for your React applications with interactive maps and geolocation features.",
      url: "/map/leaflet-map",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <LeafletMapPage />
    </div>
  );
};

export default Page;
