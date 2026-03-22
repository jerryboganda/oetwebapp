import React from "react";
import LandingPage from "@/app/other-pages/(landing)/_components/LandingPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Landing Page - PolytronX",
    description:
      "Modern and responsive landing page for your React application.",
    keywords: [
      "landing page",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "homepage",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "landing design",
      "page structure",
      "platform integration",
      "marketing page",
    ],
    openGraph: {
      title: "Landing Page - PolytronX",
      description:
        "Modern and responsive landing page for your React application.",
      url: "/other-pages/landing",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <LandingPage />
    </div>
  );
};

export default Page;
