import React from "react";
import AnimatedPage from "@/app/icons/(animated)/_components/animatedPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Animated Icons - PolytronX",
    description:
      "Explore animated icons for enhanced UI interactions in your React applications.",
    keywords: [
      "animated icons",
      "react icons",
      "UI icons",
      "icon animations",
      "react components",
      "icon customization",
      "icon effects",
      "icon integration",
      "icon library",
      "react optimization",
      "icon performance",
      "UI animations",
      "icon styles",
      "icon customization",
    ],
    openGraph: {
      title: "Animated Icons - PolytronX",
      description:
        "Explore animated icons for enhanced UI interactions in your React applications.",
      url: "/icons/animated",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AnimatedPage />
    </div>
  );
};

export default Page;
