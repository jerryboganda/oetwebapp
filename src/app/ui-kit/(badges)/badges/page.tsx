import React from "react";
import BadgesPage from "@/app/ui-kit/(badges)/_components/BadgesPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Badges - PolytronX",
    description:
      "Explore badge components for status indicators and notifications in your React application.",
    keywords: [
      "badges",
      "react components",
      "UI components",
      "status indicators",
      "notifications",
      "react badge",
      "UI badge",
      "component customization",
      "react optimization",
      "component performance",
      "badge design",
      "UI integration",
      "status components",
      "notification components",
    ],
    openGraph: {
      title: "Badges - PolytronX",
      description:
        "Explore badge components for status indicators and notifications in your React application.",
      url: "/ui-kit/badges",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <BadgesPage />
    </div>
  );
};

export default Page;
