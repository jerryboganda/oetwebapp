import React from "react";
import PhosphorIconPage from "@/app/icons/(phosphor)/_components/phosphorIconPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Phosphor Icons - PolytronX",
    description:
      "Explore the Phosphor icon library for your React applications.",
    keywords: [
      "phosphor icons",
      "react icons",
      "UI icons",
      "icon library",
      "react components",
      "icon customization",
      "icon effects",
      "icon integration",
      "react optimization",
      "icon performance",
      "icon customization",
      "icon styles",
      "icon customization",
    ],
    openGraph: {
      title: "Phosphor Icons - PolytronX",
      description:
        "Explore the Phosphor icon library for your React applications.",
      url: "/icons/phosphor",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <PhosphorIconPage />
    </div>
  );
};

export default Page;
