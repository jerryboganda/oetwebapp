import React from "react";
import SitemapPage from "@/app/other-pages/(sitemap)/_components/SitemapPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Sitemap - PolytronX",
    description:
      "Sitemap page for organizing and displaying your React application's content structure.",
    keywords: [
      "sitemap",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "navigation page",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "sitemap design",
      "page structure",
      "platform integration",
      "site navigation",
      "content organization",
    ],
    openGraph: {
      title: "Sitemap - PolytronX",
      description:
        "Sitemap page for organizing and displaying your React application's content structure.",
      url: "/other-pages/sitemap",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <SitemapPage />
    </div>
  );
};

export default Page;
