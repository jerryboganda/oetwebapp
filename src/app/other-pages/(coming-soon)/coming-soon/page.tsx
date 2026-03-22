import React from "react";
import ComingSoonPage from "@/app/other-pages/(coming-soon)/_components/ComingSoonPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Coming Soon - PolytronX",
    description:
      "Coming soon page for announcing new features or launches in your React application.",
    keywords: [
      "coming soon",
      "launch page",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "announcement page",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "launch design",
      "page structure",
      "platform integration",
    ],
    openGraph: {
      title: "Coming Soon - PolytronX",
      description:
        "Coming soon page for announcing new features or launches in your React application.",
      url: "/other-pages/coming-soon",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <ComingSoonPage />
    </div>
  );
};

export default Page;
