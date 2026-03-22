import React from "react";
import FlagIconpage from "@/app/icons/(flag)/_components/FlagIconpage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Flag Icons - PolytronX",
    description:
      "Explore flag icons for country and region representation in your React applications.",
    keywords: [
      "flag icons",
      "react icons",
      "UI icons",
      "country flags",
      "region flags",
      "react components",
      "icon customization",
      "icon effects",
      "icon integration",
      "icon library",
      "react optimization",
      "icon performance",
      "flag customization",
      "icon styles",
      "icon customization",
    ],
    openGraph: {
      title: "Flag Icons - PolytronX",
      description:
        "Explore flag icons for country and region representation in your React applications.",
      url: "/icons/flag",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FlagIconpage />
    </div>
  );
};

export default Page;
