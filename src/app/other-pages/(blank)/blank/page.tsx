import React from "react";
import BlankPage from "@/app/other-pages/(blank)/_components/BlankPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Blank Page - PolytronX",
    description:
      "A clean, minimal blank page for building custom pages in your React application.",
    keywords: [
      "blank page",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "custom page",
      "page customization",
      "page layout",
      "workspace customization",
      "react optimization",
      "page performance",
      "workspace design",
      "page structure",
      "platform integration",
    ],
    openGraph: {
      title: "Blank Page - PolytronX",
      description:
        "A clean, minimal blank page for building custom pages in your React application.",
      url: "/other-pages/blank",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BlankPage />
    </div>
  );
};

export default Page;
