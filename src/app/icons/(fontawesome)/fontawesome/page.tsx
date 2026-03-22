import React from "react";
import FontawesomPage from "@/app/icons/(fontawesome)/_components/FontawesomPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "FontAwesome Icons - PolytronX",
    description:
      "Explore the comprehensive FontAwesome icon library for your React applications.",
    keywords: [
      "fontawesome icons",
      "react icons",
      "UI icons",
      "icon library",
      "font icons",
      "react components",
      "icon customization",
      "icon effects",
      "icon integration",
      "react optimization",
      "icon performance",
      "font customization",
      "icon styles",
      "icon customization",
    ],
    openGraph: {
      title: "FontAwesome Icons - PolytronX",
      description:
        "Explore the comprehensive FontAwesome icon library for your React applications.",
      url: "/icons/fontawesome",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <FontawesomPage />
    </div>
  );
};

export default Page;
