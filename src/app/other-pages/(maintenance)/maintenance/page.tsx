import React from "react";
import MaintenancePage from "@/app/other-pages/(maintenance)/_components/MaintenancePage";
import type { Metadata } from "next";

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Maintenance Page - PolytronX",
    description:
      "Maintenance page for temporary site downtime in your React application.",
    keywords: [
      "maintenance page",
      "react workspace",
      "UI workspace",
      "page",
      "react components",
      "downtime page",
      "page customization",
      "workspace customization",
      "react optimization",
      "page performance",
      "maintenance design",
      "page structure",
      "platform integration",
      "system maintenance",
    ],
    openGraph: {
      title: "Maintenance Page - PolytronX",
      description:
        "Maintenance page for temporary site downtime in your React application.",
      url: "/other-pages/maintenance",
      siteName: "PolytronX",
      locale: "en_US",
      type: "website",
    },
  };
}

const Page = () => {
  return (
    <div>
      <MaintenancePage />
    </div>
  );
};

export default Page;
