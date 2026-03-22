import React from "react";
import AlertPage from "@/app/ui-kit/(alert)/_components/AlertPage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Alerts - PolytronX",
    description:
      "Explore alert components for displaying notifications and messages in your React application.",
    keywords: [
      "alerts",
      "react components",
      "UI components",
      "notifications",
      "message display",
      "react alert",
      "UI alert",
      "component customization",
      "react optimization",
      "component performance",
      "alert design",
      "UI integration",
      "notification components",
      "message components",
    ],
    openGraph: {
      title: "Alerts - PolytronX",
      description:
        "Explore alert components for displaying notifications and messages in your React application.",
      url: "/ui-kit/alert",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <AlertPage />
    </div>
  );
};

export default Page;
