import React from "react";
import ScrollpyPage from "@/app/advance-ui/(scrollpy)/_components/ScrollpyPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Scrollpy - PolytronX",
    description:
      "Explore advanced scroll effects and animations for your web applications.",
    keywords: [
      "scrollpy",
      "scroll effects",
      "scroll animations",
      "react scroll",
      "scroll component",
      "scroll behavior",
      "scroll events",
      "scroll triggers",
      "scroll animations",
      "scroll effects",
      "scroll customization",
      "scroll integration",
      "scroll library",
      "react scroll effects",
      "scroll performance",
      "scroll optimization",
    ],
    openGraph: {
      title: "Scrollpy - PolytronX",
      description:
        "Explore advanced scroll effects and animations for your web applications.",
      url: "/advance-ui/scrollpy",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ScrollpyPage />
    </div>
  );
};

export default Page;
