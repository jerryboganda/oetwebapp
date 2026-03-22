import React from "react";
import GoogleMapsPage from "@/app/map/(google-map)/_components/GoogleMapsPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Google Maps - PolytronX",
    description:
      "Explore Google Maps integration for your React applications with interactive maps and geolocation features.",
    keywords: [
      "google maps",
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
      title: "Google Maps - PolytronX",
      description:
        "Explore Google Maps integration for your React applications with interactive maps and geolocation features.",
      url: "/map/google-maps",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <GoogleMapsPage />
    </div>
  );
};

export default Page;
